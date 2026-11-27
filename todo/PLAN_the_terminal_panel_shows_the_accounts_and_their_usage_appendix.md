# PLAN — appendix to "the terminal panel shows the AI accounts of the open ssh hosts, and their usage"

> Status: **appendix of an open plan, in two parts (2026-11-19 and 2026-11-25); its plan stopped at entries 2.2
> and 2.3, was revised for epoch 3, and the owner approved epoch 3 on 2026-11-26.** It belongs to
> [PLAN_the_terminal_panel_shows_the_accounts_and_their_usage.md](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage.md)
> and is promoted with it. **Part 1, §§ A–H (2026-11-19).** The plan was 154 KB when the plan gate read it on
> `653140c3`, and one reviewer could not take it whole. What is here was moved out of it **word for word**, so that
> nothing is lost: only the headings were put one level down. § E also holds new text: the gate's findings 1, 2 and 6
> on the installer. Where a moved section speaks of "this plan", "§ 3" or "below", it means the plan as it stood at
> `653140c3`; where the plan of today says something else, the plan of today holds. Sections R1–R11 and epics E0–E2
> named in the moved text are those of that revision. **Part 2, § I (2026-11-25).** The plan was 138 KB when it
> stopped at `07370c49`. The revision for epoch 3 rewrote it whole, and its text at the stop — the plan that epoch 2
> approved and that epic E1 built (#1047, `d0241e45`) — is § I, word for word. **§ J (2026-11-25)** is one section
> of the revised plan, moved word for word to keep the plan under 100 KB: what the code does on `d0241e45`.
> **§ K (2026-11-26)** is six more passages, moved word for word when the approval of epoch 3 added the read of
> root, and ten more (K.7 to K.16), moved later that day when W9 was run; § J also holds, marked as added, the
> owner's answers of that day. Seven more (K.17 to K.23) were moved with the approval of epoch 4, and ten more
> (K.24 to K.33) with the ledger entry 4.1, both on 2026-11-26. Seven more (K.34 to K.40) came with entries
> 4.2 and 4.3 that day, and four (K.41 to K.44) with the entries of epoch 5 on 2026-11-27. Eleven more
> (K.45 to K.55) came with entries 6.2 and 6.3 the same day, and four (K.56 to K.59) with entry 7.1.

| § | What it holds | Why it is not in the plan |
|---|---|---|
| A | The owner's decisions 1–20 in the owner's words, and the questions O1–O7, N1–N5, Q1–Q2 with the planner's recommendation beside each answer | The plan keeps one line for each decision |
| B | Findings F1 and F2 in full | The plan keeps the statement and the judgement |
| C | The judgement on road (b), the `curl` road | Road (b) is not built |
| D | The plan's tables of step 0 and W1–W5, and what did not go as written in step 0 | The record is the RESULTS file; the plan keeps what the design uses |
| E | The `jq` installer: its design as reviewed, the gate's findings 1, 2 and 6 folded in, its tests, its real-host checks, its part of Security and Growth | It is candidate C1, built after E2 |
| F | Background: the agent-models mod's usage read, what coai measured about Codex and agy on 2026-10-10 | History, and road (b) |
| G | § 3, the consent, as reviewed: with the reasons for one store, for the settings' names and for their scope | The plan keeps the rules and one sentence of each reason |
| H | Three passages of § 1 and § 4 in full: why a QuickPick, why the working folder `/`, why `TZ=UTC` | The plan keeps the rule and the measured fact of each |
| I | **Part 2.** The plan as it stood at the stop (`07370c49`, 2026-11-25): its status block and every section after the ledger — the goal, the owner's decisions, what the code did, § 1 to § 5 of the design, what the plan does not do, Security with the eight residuals of the refusals file, Growth, the boundaries, the build order, the test plan, the Definition of Done and the epics with R1–R12 | The revision for epoch 3 rewrote the plan whole. The plan of today cites § I for what epic E1 built and what stays in force: the refusals file, its lock and its markers, the script and its reply, the tests of E1 |
| J | From the plan as revised for epoch 3 (`3a42eebc`): *What the code does today — verified on `d0241e45`*, with its file and line references, *What was measured, and what the real window showed*, the questions Q1–Q4 as they were asked (the owner answered Q1–Q3 on 2026-11-25; the plan holds the answers), and the owner's decisions 1–20, each with how it stands after the stop | The plan was 98 KB and the plan round's five findings needed room; the plan keeps a short list and cites § J. **What § J says of a per-host "yes" and of Q1's other way was overtaken by the plan round's finding 0: every door needs the permission** |
| K | From the plan as approved for epoch 3 (`2df01bb4`), moved on 2026-11-26: the short list of what the design stands on in the code, why the build order is as it is, why the epics are split as they are, what the time table of revision 2 did not take, the security judgements of 2026-11-25, and the cut of *Build order* step 1 as it was planned | The owner's answer "root too" at the approval added the read of root to the plan; these six passages are reasons and records, and the plan keeps every rule. **K.7 to K.16** were moved later the same day, from `38be6b9b`, when W9 was run: what the row of W9 measures, the picture of the page, the judgements with the one on root as a place, the judgement on the tab, the candidates, four short lists of what E1 built, the owner's three sentences at the real window, the reasons of the cornerstone areas, the judgement on the permission, and two items of *What an attacker gains* |

## § A — The owner's decisions in full, and the questions with their answers

#### Owner decisions (2026-11-19)

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
    answer is "yes", and not when it is "no" or forgotten. A host with no answer was not read in that revision; the owner then
    decided that it is (item 18).
15. **N3 — answered by the run** of W1–W5.
16. **N4 — "With the version."** The list shows the Antigravity version: the script starts `agy` with its version flag
    in every round. The risk the planner named, a self-update on a start, is recorded in Security and Growth.
17. **N5 — "Install what is needed yourself, and that is all"; then: "and only if there are no rights, write to the
    person: run this command".** Where `jq` is missing, the extension installs it, when the ssh user may. Where the
    user may not, the list tells the person the exact command to run, and runs nothing. **This reverses the earlier
    line "nothing is installed on a VM".** The design is in § 4 and the judgement is finding F2.

**The owner's answers to Q1 and Q2 (2026-11-19; given in Russian, translated here).**

18. **Q1 — "New hosts are allowed by themselves."** A host of an open `Creds:` terminal that has no answer is read by
    the round, with no modal and no press. Once a day one notice, with no question, lists the hosts that were allowed
    by themselves and says where to forbid them. An explicit "no" (*Not this host*) is never overridden. The planner's
    recommendation was to keep the question; the owner decided otherwise, and § 3 is designed on the owner's answer.
19. **Q2 — "Install at once."** The install of `jq` runs by itself, as N5 says, also on a host whose "yes" is older
    than this release and on a host that was allowed by itself. No press is needed. The planner's recommendation was
    to wait for one press on an old "yes"; the owner decided otherwise.

**Owner decision 20 (2026-11-19; given in Russian, translated here).** The main session put the risk of findings F1
and F2 to the owner in plain words — any program can name a terminal `Creds: prod-db / x`, and that leads to an
automatic ssh login and an install as root — and offered two settings that are off by default.

20. **"Yes, but on by default."** There are two settings, and both are ON by default:
    `coai.terminalAccountsAllowNewHosts` (a host with no answer is allowed by itself) and
    `coai.terminalAccountsInstallMissingTools` (`jq` is installed where it is needed). Turning the first off restores
    ask-first; turning the second off restores no-install. Only a value in the person's own user settings counts.
    The planner's recommendation stays recorded: off by default (finding F1, finding F2).

#### Open points for the owner

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

**Q1 and Q2 are answered (2026-11-19). No question for the owner is open.** They came from N2 and from finding F2.
In both the owner decided against the planner's recommendation, and the plan follows the owner.

| # | The question | The planner recommended | The owner answered |
|---|---|---|---|
| Q1 | **A host with no answer yet.** Should the round ask once, by a modal, when it first meets a host of an open `Creds:` terminal that has no answer? The default then was: no question, and the host is not read until a press on its row asks | **Keep that default.** A modal that a terminal's NAME can set off, with no press, is what the terminal-door plan avoided: any program can open a terminal called `Creds: x / y`. And with five hosts it is five modals at the start | **Neither: "new hosts are allowed by themselves."** No modal and no press: the round allows a new host and reads it. Once a day a notice with no question names the hosts allowed that way and says where to forbid them. A "no" is never overridden (§ 3, finding F1) |
| Q2 | **An install on an old "yes"** (finding F2). On a host whose "yes" was given before this release — to *"Read the Claude session…"* — should the install of `jq` wait for one press on that host's row? | **Yes, wait for the press on an old "yes".** For the owner that is at most one press for each host without `jq`, once. For another person it is the difference between software installed as root on their server after an update, and a row that offers it | **No: "install at once."** The install runs by itself on every allowed host where it is needed and the ssh user may — an old "yes" and an automatic one included (§ 4, finding F2) |

**After Q1 and Q2 the main session put findings F1 and F2 to the owner again, with two settings that are off by
default.** The owner answered: *"Yes, but on by default"* (decision 20). The settings are in § 3. The planner's
recommendation, off by default, is recorded in both findings.

## § B — Findings F1 and F2 in full

**Finding F1, for the owner: the consent no longer comes first. An old "yes" is used with no press (N2), and a host
nobody was asked about is allowed by a terminal's name (Q1).**

**On by default, as the owner chose (decision 20). Turning `coai.terminalAccountsAllowNewHosts` off restores
ask-first:** no host is contacted without a person's "yes", and an automatic row counts as no answer. What follows
describes the extension as it ships, with the setting on. The planner's recommendation was off by default, and it
stays recorded here: with the default on, a person who never opens the settings is in the position this finding
describes.

- **No rule of `.agents/` forbids it.** `common/security.md` has no rule on consent. The conflict is with texts of
  this repository that say the opposite, and this plan changes them openly:
  1. `SECURITY.md`, *The extension's one outward ssh call*: *"Read-only, to a host the person agreed to. The first read
     for a host asks once (a modal naming the host)"*; *"No ssh child is started before a "yes""*; *"a name is a
     request, never proof"*.
  2. The terminal-door plan: *The person agrees to each host once*, and in its Security section: *"The terminal name
     chooses the host, and a name is not proof, which is why the consent exists."*
- **Three widenings, in the order the owner decided them.**
  1. **O3:** a "yes" that was given to one read — *"Read the Claude session…"*, started by a press — also covers
     another kind of read.
  2. **N2:** and with no press. After the update the same row is used by a timer from the first start of the
     extension.
  3. **Q1:** and a host with NO answer is allowed by itself. From then on the store does not stand before the
     accounts read at all; it only remembers refusals. Because the row is shared, the session reader's own question
     is in practice not asked any more while the round runs (§ 3).
- **What a terminal's name can now set off.** Any program that can open a terminal in the window — another
  extension, a task of an opened workspace — can call it `Creds: <host> / x`. Within 30 s to 10 minutes the extension
  then logs in to that host over ssh, as the person, with the person's keys; runs the fixed script there; and
  installs `jq` when F2's conditions hold. **What limits it:** the name chooses only the host, from an alphabet with
  no option and no shell character; ssh refuses a host whose key is not already in the person's `known_hosts`, never
  asks for a password and forwards no agent; the script is fixed; and nothing of the reply goes back to whoever
  opened the terminal. **What does not limit it:** every host the person has ever logged in to with a key can be
  reached this way, a production server among them.
- **For the owner's own installation, O3, N2 and Q1 are the consent**, given on 2026-11-19 knowing what they cover.
  The extension is public. For any other person, after an update: nothing asks them, about an old "yes" or about a
  new host.
- **What is left to tell them, since nothing asks them:** the daily notice of the hosts that were allowed by
  themselves; the one notice at the first round; the list and the hosts list, which say how each host is allowed; the
  stop button; the help in five languages, `SECURITY.md` and the release notes (§ 3). All of it comes after the first
  read, and a notice can be missed.
- **The planner recommended a separate consent, then a round that waits for one press, then to keep the question for
  a new host. The owner decided otherwise each time, and this plan follows the owner.**

**Finding F2, for the owner: the install changes the machine, as root, at once (Q2) — and with Q1, on a host nobody
was asked about.**

**On by default, as the owner chose (decision 20). Turning `coai.terminalAccountsInstallMissingTools` off restores
no-install:** no install call is made, and the row shows the command for the person to run. What follows describes
the extension as it ships, with the setting on. The planner's recommendation was off by default, and it stays
recorded here.

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
- **F1 and F2 together: what happens to another person who updates the extension.** They open a `Creds:` terminal to
  a server, as they did the day before. Nothing asks them anything, then or later. Within minutes the extension logs
  in to that server. If Claude Code or Codex is found there, `jq` is missing, and their ssh user is root or has `sudo`
  without a password, a package is installed as root, and its scripts run as root. The first they hear of it is a
  notice afterwards. If the terminal's name came from a program and not from them, the same happens on a host they
  did not choose. Nobody agreed to that, and the stop button is too late for it.
- **What the install can do beyond adding a small program.** It runs the package's own scripts as root. It can run
  the distribution's hooks; on some systems a hook restarts services that were waiting for a restart. Refreshing the
  package lists changes what a later upgrade by the person sees. On a machine with several Linux users — and two
  aliases can be one machine — an install through one alias changes the machine for every user of it. The package
  comes from whatever repositories the host is set up with, checked as the host's package manager checks them; we add
  no repository and switch no check off.
- **What this plan does inside the owner's decisions:** the smallest install there is — one package, the host's own
  manager by a fixed path, a fixed command from a closed table, no password ever, once for each host, a record
  written before the call; **only where it is needed** — a host with no Claude Code and no Codex gets no install, so
  a server that a terminal merely names is logged in to and not changed; a notice and a list row for every install;
  the new words of the question, the help and `SECURITY.md`; and, where the user has no rights, a command for the
  person and nothing run.
- **The planner recommended that an install on an old "yes" waits for one press (Q2). The owner decided "install at
  once", and this plan follows the owner.**

## § C — The judgement on road (b)

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

## § D — The plan's tables of step 0 and W1–W5

#### What step 0 measured — 2026-11-19, on `ai2-root`

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

#### 5. Step 0 — the measurement: done 2026-11-19; W1–W5 measured the same day; one check and two records owed

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

#### E0 — Step 0, the measurement · done 2026-11-19

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

## § E — The `jq` installer (candidate C1)

**The gate's findings on the installer, folded in (2026-11-19).** The design under this block is the text the gate
read. These three points change it. The spec of C1 is written from both, and where they differ, these points hold.

1. **One install at a time for a host, across windows (finding 1).** A record row in `globalState` is not a lock: two
   windows can both find no row and both start. So there are two claims, and the one on the VM decides.
   - **On the VM.** The first act of the install script is `mkdir` of one lock folder in the ssh user's own home
     (`~/.cache/coai-install-jq.lock`; the spec fixes the path). `mkdir` makes the folder or fails; it cannot do
     half. The script that made it holds the claim and writes its start time inside. A script that finds the folder
     there installs nothing: it replies `busy`, with the age of the lock. The holder removes the folder when its
     sequence has ended — also after a failure and after its deadline (a `trap`).
   - **Locally.** The `started` row is written by the same commit as the consent store's (the plan, § 3): it re-reads
     the record, and writes `started` only when the fresh record has no row for that host. A window whose commit
     finds a row makes no call.
   - **Two Linux users of one machine** have two lock folders. The package manager's own lock then decides, and the
     second user's script finds `jq` present.
   - **Tests.** Two install calls at once against one container: exactly one runs the package manager, the other
     replies `busy`. Two windows over one record, one of them with a stale view: one call.
2. **One deadline for the whole sequence, enforced on the VM, and no retry while an earlier one may still run
   (finding 2).** The refresh of the lists, the install and the second try are ONE sequence under ONE `timeout` on the
   VM: 150 s, and a kill of its group 10 s later. The earlier text gave the limit to one command. The extension's
   ssh ceiling stays above it, at 180 s, so the VM's deadline is the one that ends the sequence and the extension
   normally reads its reply. When the ssh connection drops, or the extension ends its child, the sequence on the VM
   goes on until its own deadline, and the lock folder stays for that time. **A retry — which only a press can ask
   for — is refused while the lock folder exists and is younger than 200 s**: the row says that an install is still
   running on that host. A lock that is older is stale (its holder was ended before its `trap` ran): the row says so,
   with its age; only a press removes it and tries again, never a timer. **Observed, not assumed:** the container
   tests end the ssh client in the middle of an install, and let the deadline pass, and then list every process of
   that user. None of the sequence may be left after the deadline, and the lock is gone or stale as written here.
3. **The install record keeps its guard (finding 6).** The earlier text capped the record at 100 rows and let the
   oldest row go. A row that goes takes the "once" guard and the history with it. Now the record holds up to 1000
   rows, about 100 KiB; no row is ever dropped to make room; at the cap a new host gets no automatic install — its
   row shows the command — and the list says that the record is full. *Changes CoAI made on a host* shows every row.

**What E1 and E2 already do for a host without `jq`.** The accounts script names the host's package manager with one
closed word, and the row shows the fixed command (the plan, § 4). The installer adds the automatic step in front of
that, and the setting `coai.terminalAccountsInstallMissingTools` that turns it off.

### The design as the gate read it

**Installing `jq` — a second script, the only one that changes a VM (the owner's N5).**

- **When — at once, and only where it is needed (N5, Q2).** A host's read says that Claude Code or Codex was found
  there and that `jq` is missing (`unreadable no-jq`); the host's answer is "yes", whether a person gave it, it is
  older than this release, or the round wrote it by itself; the round is not stopped; and the install record has no
  row for that host. Then the round makes ONE install call to that host, right after its read, with no press, and
  reads the host once more when the reply says `installed`. **A host on which neither Claude Code nor Codex was found
  gets no install**: `jq` would read nothing there. This is the planner's reading of "install what is needed", and it
  keeps a server that has no AI CLI — one that a terminal merely names — at a read-only login.
- **With `coai.terminalAccountsInstallMissingTools` off, none of this runs.** No install call, no record. The row
  says `jq` is missing and shows the fixed command for the manager word the accounts script sent. The table below is
  then only a table of sentences.

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

### Its tests, as the gate read them

- **The install, decided on this side** (pure, with a seam for the call and for the store): no install call without a
  `no-jq` reply, to a refused host, to a host where neither Claude Code nor Codex was found, or while the round is
  stopped; an install call with no press on a "yes" of any kind — a person's, one older than the release, an
  automatic one; the row is `started` in the store BEFORE the call
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

### Its real-host checks, as the gate read them

| # | Check |
|---|---|
| R10 | **The install, on real hosts.** (a) A VM or container without `jq`, reached as root or with `sudo` without a password: within the first round `jq` is installed once; the notice and the row *installed jq on this host* appear; the next read shows the accounts; the VM's package log shows one install of `jq` and nothing else installed, upgraded or removed; no service was restarted; no second install call in the next rounds (the ssh log). (b) A Linux user with no rights: nothing is installed; the row shows the exact command for that VM's manager; the copy button gives that string; after the owner runs it there, the next round reads the accounts. (c) After a reload in each state: no new install call. (d) A host with no `jq`, no Claude Code and no Codex: it is logged in to and nothing is installed. (e) The install on a host that was allowed by itself, with no press at any point |
| R11 | **The two settings.** With `coai.terminalAccountsAllowNewHosts` off in the user settings: a new `Creds:` terminal's host is not contacted (the VM's ssh log) and says *not asked yet*; a press asks; a host that was allowed automatically before is not contacted either, until *Read* is pressed. With `coai.terminalAccountsInstallMissingTools` off: on a host without `jq` nothing is installed and the row shows the command. A workspace `settings.json` that sets either key the other way changes nothing, and the list or VS Code's Settings editor says it is ignored. The same in a WSL or remote window, if the owner uses one. The daily notice names both settings and its button opens them |

### Its part of Security and Growth, as the gate read it

| What | Where it is | What our script does with it |
|---|---|---|
| The package manager's output (it can hold the addresses of private repositories, and a token inside one) | the install call, on the VM | Dropped on the VM. The reply holds closed words and one exit code |
| Root on the VM | the install call | Used for one fixed command from a closed table, by a fixed absolute path, only when the ssh user already is root or has `sudo` without a password. Our script holds no password and asks for none |

- **The install, seen from each of them.** *That Linux user, or root:* the install gives them nothing; it runs only
  where they already are root or have `sudo` without a password. A false `jq` on their `PATH` only hides that `jq` is
  missing. *Another Linux user of the machine:* the manager is run by a fixed absolute path, never from the `PATH`, so
  a file they plant in a folder they can write is not run as root. *Someone on the network, or whoever controls a
  repository the host trusts:* they are in the position they are in whenever the person installs a package by hand;
  the install is one more such moment, at a time the person did not choose. *A hostile VM:* its reply can only name
  one of four managers or one of a few states. That chooses among fixed strings of the extension; it cannot put a
  word into the command the list shows. It can say "no `jq`" on every read; the install is still called once.
- **The record of installs**: one `globalState` list, a row for each host where an install was tried, about 100 bytes
  a row, capped at 100 rows; at the cap the oldest row goes, and that host can then be tried once more. Under 12 KiB.
  A row left at `started` by a reload or a crash is not swept: it is shown as it is, and a press tries again.
- **On the VM, by the install**: the package `jq` and what its manager adds as dependencies — a few megabytes, not
  measured — once for each machine, and refreshed package lists where the first try failed. Nobody retires them: they
  stay until the person removes the package. *Changes CoAI made on a host* is the record of it.

## § F — Background

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

## § G — § 3, the consent, as the gate read it

#### 3. The consent — one shared answer for each host, and a new host is allowed by itself (O3, N2, Q1)

**Decisions (the owner, 2026-11-19).** O3: there is no separate consent; the per-host answer of the session reader is
the answer for the accounts read too. N2: the round runs by itself over every distinct host of the open `Creds:`
terminals. Q1: a host with no answer is allowed by itself. N5 and Q2: where `jq` is missing, it is installed, at once.

**What the store is from this release.** Until now `coai.terminalHosts` held the answers to a question, and no ssh
child started before a "yes". From this release it is two things: **the list of hosts the person refused, which
nothing overrides**; and a record of the hosts that are read, with how each one came to be allowed. For the accounts
read it no longer comes first. Only a "no" stops a host.

| The host's row | What the round does |
|---|---|
| "yes", written by a press (*Read*) | Reads it, from the first round after the extension starts. No press and no question. Installs `jq` there at once when it is needed (§ 4) |
| none — a new host, or a host after *Forget* | **Allows it by itself**, while `coai.terminalAccountsAllowNewHosts` is on (the default): writes a "yes" row marked as automatic, with the time, and reads the host in the same round. No modal and no press. Installs `jq` as on any allowed host. **With that setting off:** nothing; the host says *not asked yet*, and a press on its row asks |
| "no" (*Not this host*) | Nothing. No ssh call of any kind. **Never overridden**: not by the round, not by another terminal of that host, not by the cap, not by a reload |

**How a host that was allowed by itself is stored: in the same store, with a marker.** The row is
`{ host, answer: 'yes', askedUtc, how: 'auto' }`. A row written by a press has no `how`, so every row of today reads as
a person's answer. The reasons for one store and not a second one:

1. **One road in.** `consentOf` is the one function that says whether a host may be contacted. A second store would
   be a second place where a "no" has to be looked up, by every caller, every time — the shape `security.md` calls a
   measure applied at some of its sites.
2. **A "no" and an automatic "yes" cannot both exist for one host**: a host has one row.
3. **The person's three choices act on it as on any row**, with no new code path: *CoAI: hosts read over ssh* shows
   *allowed automatically on <day>*, and *Not this host* turns it into a refusal.
4. **The marker is what the rest needs**: the list can say how a host came in, the daily notice knows which hosts to
   name, and the cap can tell a person's answer from an automatic one.

**The cap protects the person's answers.** The store holds 100 rows, and today the oldest row goes when it is full.
From this release **an automatic row never pushes out a row a person wrote**: at the cap the oldest AUTOMATIC row
goes; when there is none, the new host is not allowed and not contacted, and the list says that the list of hosts is
full. Without this rule a program that opens a hundred terminals with invented names could push a person's "no" out
of the store, and the refused host would be allowed by itself in the next round.

**The automatic "yes" covers the session read too. That is the recommended reading, and it is a widening.** The store
is shared since O3: one row, one answer. So on a host the round allowed, *take the question* and *add the question*
read the session without asking. The other reading — an automatic row that means "yes" for accounts and "ask" for a
session — makes one row mean two things, and `consentOf` would have to be told who is asking. A session read still
starts only on a press in that terminal. What it costs: the round meets a new host within 30 s to 10 minutes, so the
question *"Read the Claude session…"* is in practice no longer asked while the round runs. It is still asked when a
door is pressed on a host that has no row yet — before the round met it, or while the round is stopped. Finding F1
names this.

**What a host gets from this release, with a "yes" of any kind, with no press:**

- a read that a timer starts, from the start of the extension, every 10 minutes while a `Creds:` terminal of that
  host is open;
- the names of the accounts signed in there;
- a start of Claude Code on that host, which asks Anthropic with that host's own login;
- a start of `agy` on that host, for its version;
- **one installation of `jq` on that host, at once**, when an AI CLI was found there, `jq` is missing, and the ssh
  user is root or has `sudo` without a password.

Until this release a "yes" covered one read that a press starts. For the owner the list above is the decisions O3, N2,
N4, N5, Q1 and Q2. For another person who installed the extension, each line is wider than what they were ever asked,
and for a new host they are not asked at all. The Security section holds this as findings F1 and F2.

**What the person is told, and where. Every one of these comes after the first read, not before it.**

- **A daily notice of the hosts that were allowed by themselves (Q1).** At most one in 24 hours, and only when there
  is such a host. It is information: no question, and no button that decides anything. It lists the hosts that were
  allowed automatically since the last such notice, and says where to forbid one: *CoAI: hosts read over ssh*, the
  choice *Not this host*. It names the two settings — `coai.terminalAccountsAllowNewHosts` and
  `coai.terminalAccountsInstallMissingTools` — says that both are on, and that they are turned off in the user
  settings; its one button, *Open settings*, opens the user settings at these two keys and decides nothing. The
  notice of an install names the second setting in the same way. The first daily notice is shown as soon as the first
  host was allowed.
- **One notice, once for each installation of the extension**, at its first accounts round, for the hosts that have a
  "yes" from before: their AI accounts are now read every 10 minutes, `jq` is installed where it is missing, and
  where to stop it.
- **A notice for every installation of `jq`**, when it was made and when it failed, through the one door, so it is
  kept in the notifications ledger like every notice. The list keeps it too (*Changes CoAI made on a host*).
- **The question's words change**, where it is still asked. Same code (`read-remote-session`), same buttons, same
  store. The title becomes *"Read the Claude session and the AI accounts on “x” over ssh?"*, and the detail says what
  a "yes" covers: the lines of the list above. It does not say "read-only" without the install clause.
- ***CoAI: hosts read over ssh*** names it in each row: *the Claude session and the AI accounts are read*; *allowed
  automatically on <day>: the Claude session and the AI accounts are read*; or *not read*. Its three choices stay.
  Beside *Forget* it says that a forgotten host is allowed again by itself.
- **The accounts list** shows, for each open host, how it is allowed, and opens its three choices (§ 1).
- **The help, in five languages**: the new command; the paragraph on the per-host question, which today says the
  first read of a host asks once — that is no longer so; and the sentence *"They only read: they change nothing on
  that machine"*, which is rewritten with the one exception.
- **`SECURITY.md`** and the release notes say the same (the Security section).

**Two settings, both on by default (owner decision 20).** With both on, everything above is as written. Each one
takes one automatic step back:

| Setting | Default | On | Off |
|---|---|---|---|
| `coai.terminalAccountsAllowNewHosts` | `true` | A host with no answer is allowed by the round itself (Q1) | **Ask-first, as before Q1.** No host is contacted without a person's "yes". A host with no answer says *not asked yet*; a press on its row raises the shared question; the timer never asks. A row the round wrote by itself (`how: 'auto'`) then counts as no answer — for the accounts read and for the session read — until a person presses *Read*. The row is kept, not deleted |
| `coai.terminalAccountsInstallMissingTools` | `true` | `jq` is installed at once where it is needed (N5, Q2) | **No install.** No install call is made and no install record is written. The host's row says that `jq` is missing and shows the fixed command of § 4's table for that host's package manager, with its copy button, as the no-rights path does. Nothing is run |

- **The names.** Every one of the 68 `coai.*` settings is one flat camelCase word (`coai.kimiAllowNetworkSignIn`,
  `coai.consultEnabled`); none is a dotted group. So the names offered to the owner,
  `coai.terminalAccounts.allowNewHosts` and `coai.terminalAccounts.installMissingTools`, keep their words and lose the
  dot. "MissingTools" stays although the one tool is `jq`: the description says that today it is `jq` and nothing
  else, and another tool is a change of this plan.
- **Only the person's own user-level value counts.** A repository's `.vscode/settings.json` must not be able to turn
  either setting on for the person — or off. This repository already has the rule for a setting that switches a
  protection: `SAFETY_KEYS` in `src/modelKeys.ts` (today `kimiAllowNetworkSignIn`). Two layers hold it, and both are
  taken here:
  1. **The read goes through the user-layer door**: `inspect().globalValue`, else the manifest default — never the
     merged value. Both keys join `SAFETY_KEYS`, and the test that refuses any other read of such a key covers them.
  2. **A manifest scope.** Here it is `"scope": "application"`, as `coai.teamServers` has, and not `"machine"` as the
     Kimi key has. The Kimi key is a decision about one machine's home folder. These two are a person's decision
     about their own servers: one value for every window, which a workspace cannot set. With `machine`, a WSL or
     remote window would read its own machine's value, and a person who turned a setting off on Windows would have
     it on again there without knowing. What a remote window really reads for an application-scoped key is not
     measured here; the host scenario and R11 observe it.
- **A workspace or folder value is ignored, and said.** Where `inspect()` shows such a value, the list says so in one
  row, in the words `sideConfig.ts` uses for an ignored layer: *This workspace sets `<key>`. It is ignored: only your
  own user settings count.* If VS Code does not hand the extension that value at all for an application-scoped key,
  the row never appears and VS Code's own Settings editor says it; in both cases the value is not read.
- **Read before every call.** A setting turned off takes effect at the next call of the round. An install that is
  already running is not killed (§ 4).
- **Where each is registered:** `package.json`, with a description that says what it does, that it is on by default
  and what turning it off restores; `src/settingsShape.ts`; `SAFETY_KEYS`; `NEVER_TRANSFERRED` in
  `src/configTransfer.ts`, so an imported config file cannot decide either for the person; and the help in five
  languages, which `helpCoverage.test.ts` requires for every setting.
- **The planner's recommendation was off by default, and it stays recorded.** On by default, a person who never
  opens the settings gets both automatic steps. What the settings change is that each step can be taken back, by the
  person and by nobody else.

**The stop button is kept, and this is why.** The round is on by default and nobody has to press anything for it.
*Stop the accounts round* is one title button of the list. It clears the timer and drops the readings from memory;
while it is stopped no host is contacted for accounts or for an install, and **no host is allowed by itself**. The
reasons to keep it:

1. It is the only way to keep the session read of a host and stop the accounts read. *Not this host* stops both.
2. It is the way out when a read does harm on a VM — a Claude Code or agy update that disturbs the live sessions
   (R7 (b)) — without uninstalling the extension or closing the terminals.
3. It costs little: one button, one stored row, three tests. It is a title button and not a command or a setting,
   because each of those needs its own help entry in five languages, and a setting a control.

What it does **not** do: it does not come before the first round. The first read of every host, and an install with
it, start 30 s after the extension starts or within 10 minutes of a new terminal, before anyone can press a button.

**The state** is one `globalState` row, key `coai.terminalAccountsState`: `{ toldUtc, lastAutoNoticeUtc, stoppedUtc }`.

**How a person forbids a host — and what no longer does.**

- ***Not this host*** — in the hosts list, in a host's row of the accounts list, or in the question: no ssh call of
  any kind goes to that host: no session read, no accounts read, no install. Its reading is dropped from memory at
  once. The row stays a refusal until the person changes it.
- ***Forget* no longer stops the accounts read.** It removes the row, as before. The host then has no answer, and the
  next round allows it again by itself. A person who wants a host left alone has to say *Not this host*.
- A "no" given while a round runs takes effect before the next call (§ 2). A read that is already running ends
  within its 30 s, and its result is dropped. An install that is already running is not killed: stopping a package
  manager half-way is worse than letting it end (§ 4).

**What a person can no longer choose.** They cannot allow the session read on a host and refuse the accounts read or
the install on the same host: the per-host answer is one answer. And they are no longer asked before a host is
contacted for the first time: they can only refuse it afterwards.

**No second store, no reach number.** The first draft had a second `globalState` key and a "reach" for each row. Both
are gone with O3. The store stays `coai.terminalHosts`, with its row `{ host, answer, askedUtc }`, which gains the
marker `how`; its cap of 100 rows, which gains the rule above; and its pure functions (`consentOf`, `withAnswer`,
`withoutHost`, `answersOf`).

## § H — Three passages of § 1 and § 4 in full

- **The view is a QuickPick instance (`createQuickPick`), not a webview.** The reasons:
  1. It is a short read-only list: today five hosts and three vendors, at most a few dozen rows.
  2. It needs what `conversationPickerCommand.ts` already needs: title buttons and new rows without closing, when a
     host's read lands. That pattern is in the code and reads well.
  3. A webview is a new bundled page with its own CSP, nonce and a test that runs the page. That cost buys only colour
     and layout, which the owner did not ask for.
  4. What a QuickPick cannot do is named under *What this plan does NOT do*: no colour for each number, and a long
     row is cut in a narrow window. If the real-window check R3 shows the rows cannot be read, a webview is a plan change.

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

## § I — The plan as it stood at the stop (`07370c49`, 2026-11-25), word for word

Moved on 2026-11-25 by the revision for epoch 3. **Nothing in § I was reworded:** the status block and the lines
after the ledger were copied by a tool from the plan at `07370c49`, and only the headings were put one level down.
The ledger itself was not moved; it is in the plan, where it is kept and added to. Where § I says "this plan",
"§ 3", "above" or "below", it means the plan as it stood at `07370c49`. **Where the plan of today says something
else, the plan of today holds** — first of all for the automatic allow of a new host, its setting and its notices,
for the list as a QuickPick, and for `ssh` to a host named `wsl`, which the owner's entries 2.2 and 2.3 ended.
What stays in force as it is written here: the refusals file with its lock, its markers, its copy-in and its eight
residuals (§ 3 and Security), the script and its reply (§ 4, but for what the plan of today changes in them), and
the tests of epic E1. The names R1–R12, E1, E2 and C1–C6 in § I are those of that revision; the plan of today says
which of them it keeps.

### The status block at the stop


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
> word for word where they were moved. **STOPPED for re-review, 2026-11-25, at entries 2.2 (WSL is read on this PC) and
> 2.3 (one consent for all hosts), both the owner's at the real-window check of VSIX 2.0.901. E1 is merged (#1047,
> `d0241e45`) with `USAGE_POLICY` off. Epoch 2 was approved by the owner 2026-11-24 with change 2.1 (the S1 layout). Before it: STOPPED at 1.2; re-review `4082dc2c` `proceed` (5 findings folded into the
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

### The goal

The owner, 2026-11-19 (in Russian, translated): *"Next I want to add an icon, like the chats one. It should show a list
of all Claude Code, Antigravity and Codex accounts used in the open SSH connections in the console, and their usage.
Can we?"* Then: *"Real time is not needed. We can go over ssh every 10 minutes, in a round, and ask for the data."*

What the owner sees today: the VS Code terminal panel holds many terminals named `Creds: <host> / <session>`, for
example `Creds: ai1-dev2 / alert-multiply-teams`, `Creds: ai1-root / ba-gate1`, `Creds: ai2-dev2 / orc-coai`,
`Creds: ai2-root / orc-user-profile`. That is five hosts: `ai1-dev2`, `ai1-dev3`, `ai1-root`, `ai2-dev2`, `ai2-root`.
Each host is a different Linux user with its own `~/.claude` (the terminal-door plan, owner decision 5). The panel's
title bar has a "+" and coai's *Ask other AI* icon. Nothing in VS Code says which account each host is signed in to,
or how much of its limit is left. To learn it the owner opens each session and asks there.

### Owner decisions (2026-11-19)

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

### What the code does today — verified on `a057c971` (extension 0.89.1)

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

#### What step 0 and W1–W5 measured — 2026-11-19, on `ai2-root`

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

### The design

#### 1. The view

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

#### 2. The round

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

#### 3. The consent — one shared store, a "no" that always wins, and a new host allowed by itself (O3, N2, Q1)

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

#### 4. The remote script and its reply — the cornerstone

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

#### 5. What is still to be measured — in E2, on the owner's hosts

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

### What this plan does NOT do

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

### Security

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

### Growth surfaces

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

### Boundaries with other plans

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

### Build order

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

### Test plan

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

### Definition of Done

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

### Epics

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

#### E1 — The read-only feature, end to end

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

#### E2 — Run it for real, on the owner's hosts

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

#### Candidates after E2

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

## § J — What the code does on `d0241e45`, and what was measured: from the plan as revised for epoch 3 (`3a42eebc`), word for word

### What the code does today — verified on `d0241e45` (main, after #1047)

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
  (`package.json:288`; its `view/title` entry at `:1046`, `navigation@91` at `:1048`), grouped by account (`accountsRows`,
  `src/remoteAccountsRows.ts:408`). It is drawn again only when a read lands, so *read just now*
  (`readAge`, `:105`; `agoWords`, `src/gaugeWords.ts:92`) stays as it is between two rounds. Two notices:
  `allowedNotice` and `everyTenNotice` (`src/remoteAccountsHost.ts:385`, `:402`).
- **A tab with a table exists once, and is the pattern.** `RoundsLogPanel` (`src/roundsLogPanel.ts`): one
  `WebviewPanel` for each window, revealed when it is open (`:94`, `:98`), `enableFindWidget` for Ctrl+F (`:104`), a
  nonce (`src/webviewNonce.ts`), a `ready` handshake and a record of what the page was told. A page is tested by
  RUNNING it (`.agents/PROJECT.md`; `src/test/bundledPage.test.ts`, `src/test/pageDom.ts`).

#### What was measured, and what the real window showed

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
  ceiling (PLAN_the_usage_mode_stays_under_the_30_s_ceiling.md (a deleted todo file));
  T9 (PLAN_a_press_rereads_the_consent_before_each_spawn.md (a deleted todo file));
  T8 (PLAN_a_waiting_marker_has_its_own_door_sentence.md (a deleted todo file)).
  This revision takes the three up: T46 in § 4, T8 and T9 in § 3.
- **Not measured: anything about WSL.** W9 (§ 5) comes before the WSL road is specified.

### The questions Q1–Q4 as they were asked — from *Owner decisions* of the same revision, word for word

**The questions as they were asked.** The text takes the stated line for each, and Q3 is now settled as above;
an answer the other way changes the named section before epoch 3 is approved, and is no ledger entry.

| # | The question | The line this text takes |
|---|---|---|
| Q1 | Does the ONE permission also cover the read of a terminal's Claude session by the chat doors (*take the question*, *add the question*)? | **Yes** (decision 8: one consent, not two). The question's text says so. The other way: the chat keeps its per-host question, and the permission covers the accounts round only (§ 3) |
| Q2 | A WSL distro that is NOT running: a read would start it, every 10 minutes, with its services | **It is listed and never started by CoAI**; it says *not running, so not read*. Answered: not started |
| Q3 | The installer (E6) under the new consent. Decision 19 said "no press", when a per-host "yes" stood behind it. A permission worded "may I read?" cannot cover a change of a machine | **Nothing is installed under the permission to read.** Answered: one more "yes", once for all hosts, for "install `jq` where it is missing"; where the user has no rights, the command is shown |
| Q4 | The merge conditions ask the owner to check a VSIX built from the head of each of E2, E3 and E4 in a real window BEFORE that epic merges (the check of 2.0.901 came after the merge, and found three things) | **Yes**, three checks, each a short table of rows (*Epics*) |

**Added on 2026-11-26; not part of the moved passage.** The owner's answers with the approval of epoch 3, the same
day: **Q4 — yes**, as the text took it. **Root of a WSL distro is read too** ("root too"): the revision said that
root is never a place and kept a read of root's own accounts as candidate C9; the owner's answer takes it into the
approved baseline as the plan's decision 24 — root is a place of its own, read as itself. **The go for the
measurements** W9, in a disposable distro with two users on this PC, and W6 and W7, on a VM. An answer given with
the approval is part of the baseline and no ledger entry. The plan holds the answers.

### Owner decisions 1–20, each with how it stands after the stop — from *Owner decisions* of the plan as it stood at `3a42eebc` with the plan round folded in, word for word

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
19. **Q2:** the install needs no press. **Replaced by Q3 (2026-11-25): one more one-time "yes" for installs.**
20. Two settings, on by default. **The first, `coai.terminalAccountsAllowNewHosts`, is retired by 22. The second,
    `coai.terminalAccountsInstallMissingTools`, is not built: Q3's answer replaces it.** The one-time "yes" for
    installs, which a person gives and withdraws, is the one switch; a setting beside it would be a second place
    that says the same thing, and one that a settings file can change.

## § K — Six passages of the plan as approved for epoch 3, moved on 2026-11-26, word for word

The owner approved epoch 3 on 2026-11-26 with the answer that root of a WSL distro is read too (the plan's
decision 24). The text on root needed room, and the plan stays under 100 KB. These six passages were moved here
from the plan as it stood at `2df01bb4`, word for word. None is changed by the approval, and each is reasons or
a record, not a rule: the plan keeps every rule.

### K.1 — What the design stands on: the short list of *What the code does today — verified on `d0241e45`*

Paths under `src_vs_code/`. The full section is § J.

- The round's door allows a new host by itself and uses an old "yes" with no press (`src/remoteKeeper.ts:355`,
  `:376`); `consentOf` and `yesOf` answer `yes` for an automatic row at the session door too
  (`src/remoteHosts.ts:126`, `:144`). `wsl` goes to `ssh` like any host (`src/remoteSsh.ts:55`).
- The memory holds ONE entry for each host, and a reply replaces it whole (`src/remoteAccountsRound.ts:268`).
  `USAGE_POLICY` is `off` (`:39`). The script adds to the inherited `PATH` (`src/remoteAccountsScript.ts:276`), and
  its `claude-usage` path is 31 s at worst against the ceiling of 30 s (T46).
- A press reads the consent once (`src/remoteRead.ts:337`, `:348`; T9); a waiting marker reads as `damaged`
  (`src/remoteKeeper.ts:399`; T8).
- The view is a QuickPick (`src/remoteAccountsHost.ts:456`) that is drawn again only when a read lands. A tab
  with a table exists once and is the pattern: `src/roundsLogPanel.ts:94`, `:98`, `:104`.
- A refusal and the Stop are in `terminal-hosts/refused.json` of each side (`src/remoteRefusals.ts:44`).

### K.2 — *Build order*: why this order

**Why this order.** *The consent first:* main reads with no "yes" today, every later check runs through the door,
and the acceptance of entry 2.3 can be seen on the owner's 20 hosts at once. *The table with it:* the permission
needs a place to be asked and shown, and the table is how the owner checks every later epic — the QuickPick could
not show what was read. *WSL third:* it needs the place identity that the table's model already has, and a check
on this PC that can run while E2 is built. *The usage fourth:* it needs revision 2, which E3 pins, and two
measurements that depend on the owner's go and on a VM, not on our code; it is the smallest epic and the one the
owner will see most, so it merges as soon as its three conditions hold. *The long runs fifth:* two hours with live
sessions and the two-window gate prove the read-only build before anything installs; they run once more on E6's
build, which is the one released (the plan round's finding 3). *The installer
last:* it changes a machine, so the read-only path is proven first (the consultants, `f4af076c`), and its consent
is its own: one more "yes", once for all hosts (Q3).

### K.3 — *Epics*: why the split is as it is

The consultants' rule of 2026-11-19 holds for each: an epic is the smallest thing that runs end to end on real
input, and it ends with the owner seeing it in a real window. That rule is why E2 carries the consent AND the
table, and why the real runs are no longer one epic at the end: the check of 2.0.901 found three faults that a
row in a real window would have shown before the merge.

### K.4 — § 4, the time table of revision 2: what was not taken

*Not taken:* shortening the limits by 3 s in total gives exactly 28 s with every
stage in one call, and it cuts `agy`'s limit, whose real time is not measured; raising the ceiling makes a dead
host hold a round longer.

### K.5 — *Security*: the judgements of 2026-11-25

**Judgements.** *Road (a):* yes, on what is measured, with W7 as the gate that can still fail it. *The consent:*
entry 2.3 puts a person's "yes" back in front of every call, which the planner recommended on 2026-11-19 and E1 did
not have; one "yes" for all hosts is the owner's width, and the judge's condition is that the question names the
later hosts, the WSL users and the programs that are started. *WSL:* per-user runs are right and root-for-all
would be wrong; the interop residual is real and is the person's to accept with the permission; nothing is
promised as read-only before W9. *The tab:* a webview is a larger surface than a QuickPick, and it is closed by
construction — text only, no remote source, names in, fixed strings out. *The installer:* outside the permission
to read; it has a "yes" of its own, once for all hosts (Q3, answered), kept and withdrawn like the first.

The judgement on root as a place, of 2026-11-26, was in the plan, *Security*, when this was written; later the
same day it moved to K.9, below.

### K.6 — *Build order*, step 1: the cut as it was planned

The cut is merged: #1051, `b1ec467c`.

1. **The cut, during the stop** (*The full stop*, step 6: merged work that contradicts the revised plan is
   corrected in a new pull request; the owner gave the go on 2026-11-25). ONE small pull request, with four
   parts. (a) In every build from main the accounts round makes no call, by a timer or by a press: the timer is
   not armed, the round's door admits no host, nothing goes to `wsl`; the list says that reading is off until the
   permission of the next build. (b) **An automatic row counts as no answer at the SESSION door too**: today
   `consentOf` and `yesOf` (`src/remoteHosts.ts:126`, `:144`) answer `yes` for `how: 'auto'` while the setting is
   on, so a chat door reads a host nobody was asked about. (c) The help in five languages and `SECURITY.md` say
   what that build does: no host is read by itself. (d) The setting `coai.terminalAccountsAllowNewHosts` stays
   registered and says *no effect in this build*; E2 retires the key. **No extension release is cut from main
   before the cut is merged**: main holds a build that logs in to hosts with no "yes". A revert of #1047 is not
   taken: E2 to E4 build on its modules.

### K.7 to K.16 — ten more passages, moved later on 2026-11-26

These ten were moved from the plan as it stood at `38be6b9b`, word for word, on the day W9 was run. The plan
needed room for what W9 showed, for the check before a start as UID 0 (a review finding on the root rule) and
for what the security spec of E2.S1 amends, and it stays under 100 KB. As above, none is a rule of its own:
each is the text of a measurement that has been run, a picture, a judgement, a list of candidates, the owner's
words, a reason, or a short list of what E1 built — rules that the plan keeps by naming where they are (§ I
and the specs of E1). Where the plan of today says something else, the plan of today holds.

### K.7 — § 5, the row of W9: what is measured, and where

The first cell of the row. W9 was run on 2026-11-26; its record is
[../research/RESULTS_wsl_on_this_pc.md](../research/RESULTS_wsl_on_this_pc.md), with the same letters (a) to (g).

**WSL on this PC, in a disposable distro with two users**, made for the check and removed after it. (a) What `wsl.exe --list --quiet` and `--list --running --quiet` print: the encoding (UTF-16LE, or UTF-8 with `WSL_UTF8=1`), the line ends, the text and exit code with no distro and with none running, and whether a list call starts anything. (b) As each user **and as root**, through the argv of § 2: the effective UID, `HOME`, the working folder, the `PATH` as given, and whether a profile ran (a marker in each one's profile files). (c) **Before and after**: a listing — path, size, time — of both homes and of root's around the users script, around the accounts script as user A, **around it as root**, and around a read with a real Claude Code where one is installed. (d) Whether the users script answers the same as the distro's default user. (e) After `wsl.exe` is killed at the ceiling: which Linux processes are left. (f) A stopped distro: what a call does to it and how long it then stays up. (g) The time of 10 calls

### K.8 — § 1: the picture of the page

It stood under *The page is grouped by HOST*, after "in name order:".

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
    Ubuntu · user-b    no Claude Code, Codex or Antigravity found
    Ubuntu · root      read 3 min ago (14:03)
      (its table)
    Debian             not running, so not read
  ```

### K.9 — *Security*: the judgements, with the one on root as a place of 2026-11-26

**Judgements.** Those of 2026-11-25 — on road (a), the consent, WSL for each user, the tab and the installer —
stand and are in the appendix, § K.5, word for word; the judge's condition on the consent is the detail of the
question in § 3. *Root as a place, 2026-11-26:* the judgement of 2026-11-25 was not to read root, because a read
starts root's tools as root. The owner accepted exactly that (decision 24), and it is the owner's to accept: the
tools are root's own, on the owner's PC. Root-for-all stays wrong. The judge's three conditions are rules above:
root reads only as itself, with its own `HOME`; nothing that another user can write is started as root; the
permission's text says it.

### K.10 — *Security*: the tab

**The tab.** Text from a VM, a distro or a terminal's name can only become text: it is made plain at one road and
set as `textContent`; the page assembles no HTML from data; the CSP allows no source but the page's own script and
style by nonce. A message from the page carries names that the host checks against its own model; a hostile page
could at most press the tab's own buttons. The two buttons that widen what is read — *Allow reading…* and *Read
this host again* — each raise a modal question in the extension host, so no message of a page can give the
permission or lift a refusal by itself; the others only narrow. The clipboard gets a fixed string from a closed
table.

### K.11 — *Epics*: the candidates

They stood under the heading *Candidates*, the last of *Epics*.

One line each. Taking one up is a change of this plan (kind `epics`), recorded in the ledger.

- **C2 — a webview instead of the QuickPick.** Taken by this revision: it is E2.
- **C3 — places with no AI tool are read once an hour.** Justified by R10 or R13 showing such places, a distro
  with no tool and a root with no tool among them, and their reads being a nuisance.
- **C4a — the rest of the store in the file.** Gone: a per-host "yes" allows nothing any more. **C4b — a lift that is recorded, and the Stop carried to the file of every side.** The owner
  uses a second side for the chat only, so it is not justified today.
- **C5 — the Claude usage only on a press, or road (b), or the account only.** Justified by W7 failing; the
  planner recommends the account only.
- **C6 — one lock between VS Code windows for the round.** Justified by R2 showing that two windows doubling the
  logins of 20 hosts matters.
- **C7 — a stopped distro is started for a read**, on *Refresh now* or always. The owner answered Q2 "not
  started", so it is justified only by a later request of the owner.
- **C8 — the session read of a `Creds: wsl / …` terminal.** The terminal-door plan's; justified by the owner
  using the chat doors there.
- **C9 — root's own accounts in a WSL distro.** **Taken by the owner at the approval of epoch 3 (2026-11-26): it
  is in E3** (decision 24), with its security judgement in *Security*. It was taken with the approval, so it is
  baseline and no ledger entry.

### K.12 — Four short lists of what E1 built and the plan keeps: § 2, § 3, § 4 and the test plan

Each opened its section. The full text of what E1 built is § I and the two specs of E1; the plan names both.

The first paragraph of § 2, *The round*:

What E1 built stays unless a line here changes it (the appendix, § I, *2. The round*): one call at a time, each
awaited; the order rotated by one each round; 10 minutes counted from the END of a round; the checks before every
call and before every result; results in memory only; a failed place logged once when its sentence changes; no
timer while no place may be read; a round for each window.

The first paragraph of § 3, *The consent*:

**What E1 built and this plan keeps** (the appendix, § I, *3. The consent*, and the S1 spec): a person's "no" for a
host and the Stop are kept in `terminal-hosts/refused.json` of each SIDE of the machine, written by one function
under a lock that is never taken over by its age, read from the disk before every call, removed only by a person's
press; a change the file did not take is held by its window, copied to `globalState` at once and marked for every
window of the side; a damaged file contacts nobody; the "no" rows of `coai.terminalHosts` are copied in at every
read.

The first paragraph of § 4, *The scripts*:

E1's script, its reply and its parser stay as built and as specified (the appendix, § I, *4. The remote script and
its reply*, and the S2 spec): fixed text sent whole; one function read to its end and then called; `jq` names every
field that leaves; three anchored line patterns for the usage; the person's activity dropped on the VM; the Codex
record from a bounded tail; of a version command only the version; a closed manager word for a host without `jq`
and a fixed command for each; a strict frame and a closed vocabulary on this side; a missing number is `null`,
never 0. **Revision 2 changes four things, in ONE new pinned text with one new sha256** (E3.S2, the cornerstone):

The first paragraph of the *Test plan*:

**E1's tests stay** for what stays (the appendix, § I, *Test plan*): the script run for real with false programs
and the real `jq`; no secret and no activity leaves, each seen RED once; the hash; the parser; the usage lines; the
Codex record; the words; the round's order and the account-first rule; the refusals file with its lock, its
markers, the copy-in and the two checks of the cadence consultation; the runner's argv. **Deleted with what they
hold:** the tests of the automatic "yes", of the cap's eviction, of the setting at both values
(`terminalAccountsSetting.test.ts`), of the two notices, of the QuickPick's rows and its host scenario. New:

### K.13 — *The goal*: what the owner said at the real window, 2026-11-25

**What the owner said at the real window, 2026-11-25**, about the build of E1:

- *"wsl must read every WSL on my PC, all accounts."* (entry 2.2)
- *"I may have 20 hosts in the terminal, and it asks for each. No. Once: may I read? I say yes, and it reads all."*
  (entry 2.3)
- *"Something was read, but who knows what. It must be readable: which host, which AIs, which accounts, the limits,
  when they reset. Redo it."*

### K.14 — *Epics*: the cornerstone areas, each with its reason

Stories documents are written by Opus when an epic starts:
`todo/PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E<n>.md`. Cornerstone quota: Q = ⌈2·E/5⌉ =
⌈2·6/5⌉ = 3. The cornerstone areas Fable names: **(a)** what a script touches there and what it prints — the only
place a secret or the person's activity can leak, and a leak gives no error; **(b)** the reply's frame and the
parser — the only place wrong or hostile data can arrive and look right; **(c)** who is contacted and when —
nothing before the permission, a refused host never, and no stale window undoes either; **(d)** absent, signed
out, no usage source, skipped, failed and unreadable are different results, and a missing number is never a zero;
**(e)** who runs as whom on this PC — the script as the user it reads, root for the users script and for its
own place;
**(f)** a change of a machine (E6). Areas (c) and (e) are authorisation: their specs are Fable's by the security
row, outside the quota. Planned marks, made by the stories documents: E3.S2 for (a) and (b), and E6's spec for (f).

### K.15 — *Security*: the permission in front of every door

**The permission is in front of EVERY door, and a withdrawal wins like a "no"** (the plan round's findings 0 and
1). No per-host "yes" of an older build reads a host, for the round or for a chat door, before the permission or
after a withdrawal. The grant is in the locked file of the side; the withdrawal is a file of its own with a
higher stamp, which no writer of `refused.json` renames over, so a stale lock holder cannot bring a permission
back. Both are read from the disk before every call. What is left is residual 9.

### K.16 — *Security*, *What an attacker gains*: the Linux user of a place, and root in a distro

The second and third items of the list.

- **Someone who is that Linux user, in a distro or on a VM.** They can make the tab show wrong accounts or numbers
  for their own place. They cannot reach the extension through the reply: bounded (30 s, 64 KiB, 64 lines), parsed
  by a closed vocabulary, never run, never a path, drawn as text. In WSL they could already reach the Windows side
  through interop; the timer adds no right, only a start.
- **Root in a distro** can make the users script name users that do not exist; a name must fit its class and a
  UID its range, at most 16 are taken, and a call for a user that is not there fails as a call. Its own
  place is read as root, which gives it nothing. It cannot make the round read ANOTHER user as root, or a system
  account: this side too takes UID 0 only under the name `root`, and a UID from 1 to 999 never.

### K.17 to K.23 — seven more passages, moved with the approval of epoch 4 on 2026-11-26

These seven were moved from the plan as it stood at `ee7242e9`, word for word, in the commit that records the
stop of epoch 3 at its entries 3.1 and 3.2 and the owner's approval of epoch 4. The plan needed room for that
record, for the owner's decision 25 — the login session that WSL opens for a user it reads is accepted — and
for what gate E2-G1 fixed in the security spec of E2.S1, and it stays under 100 KB. As above, none is a rule
of its own: each is the text of a measurement that has been run, an item that the owner has since answered,
a judgement, a table of who builds what, or a list of how older decisions stand. The plan keeps every rule,
and says in a few lines what each passage holds. Where the plan of today says something else, the plan of
today holds — K.17 above all, whose last paragraph is an open item that is closed now, and K.22 and K.23,
which the plan of today words anew for the login session.

### K.17 — § 5: the table of the four measurements, what W9 showed, and the item W9 left open

The table as it was set before the measurements ran, with the paragraph on what W9 showed and the item that
was open for the owner until decision 25.

| # | What is measured, and where | What the result does |
|---|---|---|
| W6 | **Backups, on a VM** (unchanged; the appendix, § I, § 5). As a Linux user with no live Claude Code session: `~/.claude.json` and `~/.claude/backups/` before and after each of 12 usage reads from `/` | **Gate for growth.** It selects the usage policy of E4: a read makes no backup, or Claude Code keeps a fixed number → every round; a backup each time that nothing retires → at most once an hour for each account under a budget the owner approves, or only on *Refresh now* |
| W7 | **A trace of every program start, on a VM** (unchanged): one usage read under `strace -f -e trace=execve`, stdin `/dev/null`, from `/` | **Gate.** A program that is not Claude Code's own — a hook's command, an MCP server — and the Claude usage read stays off until the owner decides. Until W7 has run this plan does not claim "starts nothing of the person's" |
| W8 | A start of `agy --version` on a VM: its time, what it writes, what it starts | **A record** (decision 16) |
| W9 | **WSL on this PC, in a disposable distro with two users**, made for the check and removed after it. **Run 2026-11-26.** Its parts (a) to (g) as they were set: the appendix, § K.7 | **Gate for E3.** The WSL road is specified on what W9 shows. **"Read-only" is promised for WSL only as far as (c) shows**: that a read as A changes nothing in B's home or root's, and in A's only what Claude Code itself writes (decision 13); and that a read as root changes nothing in A's or B's, and in root's only what the tools themselves write. **(b) is the fact behind "root's own `HOME`"**: if a call with `-u root` can be given another, root is not read until the spec of E3.S1 closes that road. (d) decides who runs the users script, root or the default user; root's own place is read as root either way. (f) is the fact behind Q2 |

**What W9 showed**, part by part, is in that file (one Ubuntu 24.04 distro with systemd; called from inside
another distro, from a Windows process only for the `HOME` check). **(b):** no `HOME` or `WSLENV` of the caller
changes the `HOME` of a call, for root too — WSL closes that road, so root is read as far as (b) goes. **(c):**
no script changed a file in any of the three homes; no vendor tool was installed. **(d):** the same answer as
root and as the default user. **(e):** a killed call leaves no process of its own. **(f):** § 2 and residual
12. Also seen: 2 calls that failed at 30 s with WSL's own `WSAETIMEDOUT`, and none of 52 later ones. *Not
measured:* the lists with no distro and with none running; a read with a real vendor tool; a Windows process
as the caller of every call.

**Open after W9 — the owner's to choose, then the spec of E3.S1; no ledger entry.** On a distro with systemd
the first `-u <user>` call after the distro starts makes WSL open a login session for that user, root included
(`/bin/login -f`, `systemd --user`, a login `bash`). It ran the user's `~/.bash_profile`; a first login made
two empty files in that home (`.motd_shown`, `.cache/motd.legal-displayed`); it stays until the distro stops;
and the check before a start as UID 0 does not reach it. With `systemd=false` none of this happens. **So these
do not hold there as written:** "nothing as root but the users script and the read of root's own place" (*What
this plan does NOT do*, the Definition of Done); the permission's text, which names only `claude` and `agy` as
started; and "in A's only what Claude Code itself writes" (W9, above). § 2 and *Security* said "no profile is
read"; both now say what was measured. To accept the session, or to read fewer users, is a change of this
plan. **Until the owner has chosen, the spec of E3.S1 is not final and no code reads a WSL place.**

### K.18 — *Security*: finding F1 is answered, not gone

**Finding F1 is answered, not gone** (the appendix, § B). With E1 the consent no longer came first: an old "yes" was
used with no press, and a host was allowed by a terminal's name. **Before the permission nothing is contacted now,
whatever is stored and whatever a terminal is called.** After it, what F1 says of a name holds again, by the
person's own word: any program that can open a terminal named `Creds: <host> / x` makes the extension log in to
that host within 10 minutes and run the fixed script; a terminal named `Creds: wsl / x` makes it read every user
of every running distro, root among them. The name chooses the host and nothing else, from an alphabet with no option and no shell
character; ssh refuses a host whose key is not known, asks for no password and forwards no agent; nothing of a
reply goes back to whoever opened the terminal. The question says "and the ones opened later" in plain words,
because that sentence is the whole of this residual.

### K.19 — *Security*, the things named and not prevented: a user's program, the inherited `PATH`, a kill

The second, third and fourth of the list, which had four items at `ee7242e9`; the plan of today names a fifth,
the login session.

- **A program that a Linux user owns is started by our timer.** The script starts `claude` and `agy` as found in
  that user's `~/.local/bin`. Whoever can write there decides what runs — as that user, and never as another
  user: a user's program is never started as root. On a VM that ends at the VM. **In WSL it does not:** a Linux process can start Windows programs
  as the Windows user (interop). That is WSL's own boundary, and it is open to that Linux user at any time; what
  is new is that our timer starts the program with nobody at that user's keyboard. The permission's text says
  that `claude` and `agy` as found there are started.
- **The inherited `PATH`** is not used (§ 4), so a Windows program is not started by its name.
- **What a kill leaves.** No process of the call: after `wsl.exe` was killed none was left in the distro, with a
  background child and with a script on stdin too (W9e). The script's own limits end each vendor command in any
  case.

### K.20 — *Boundaries with other plans*: the table

| Item | Built by | The other plan's part |
|---|---|---|
| The pinned ssh call, the session script, the chat doors, the per-host answer `coai.terminalHosts`, the question `read-remote-session` | [PLAN_the_chat_opens_from_a_terminal.md](../research/PLAN_the_chat_opens_from_a_terminal.md) (shipped in 0.87.0; it names this plan at its line 315) | **This revision replaces the per-host consent of that plan** (Q1): a per-host "yes" allows nothing any more, the question `read-remote-session` is retired, and the chat doors read under the one permission and not without it. That changes a released behaviour: a person with per-host answers is asked once more. Kept of it: the store's key, its "no" rows as the copies of refusals, *Not this host*. Added here: the refusals file (E1), the door answer `waiting`, the re-read before every spawn |
| The todo T46, the 30 s ceiling | this plan, E3.S2 | The todo file is closed by the stories document of E3 |
| The todos T8 and T9, the session door | this plan, E2.S1 | The todo files are closed by the stories document of E2 |
| A timed-out ssh call ends its whole tree | [PLAN_a_timed_out_ssh_call_ends_its_whole_tree.md](PLAN_a_timed_out_ssh_call_ends_its_whole_tree.md) | Nothing here: it is the launcher's on Linux and macOS, and older than this plan. The WSL road needs no part of it |
| The gauge of coai's own rounds | [../research/PLAN_every_round_is_counted.md](../research/PLAN_every_round_is_counted.md) and [PLAN_the_round_counter_tail.md](PLAN_the_round_counter_tail.md) | Nothing: no shared store, no shared reader |
| The account and limits line inside Claude Code | the agent-models mod | Nothing is changed there; this plan carries its word rules over and tests them against its cases |

### K.21 — *Owner decisions*: decisions 1–20, which do not stand as they were, and which are cited

Decisions 1–20 are of 2026-11-19: the owner's words are in the appendix, § A, and the list with how each stands
after the stop is in § J, word for word. **They stand, but for these:** 4 — usage is still read once for each
account, and the view groups by HOST now (23); 8 (O3) — the one consent is the permission of 22, for both doors;
14 (N2) and 18 (Q1) — replaced by 22: nothing runs by itself before the permission, and no host is allowed by
itself; 19 (Q2) — replaced by Q3's answer; 20 — neither setting is built: `coai.terminalAccountsAllowNewHosts` is
retired by 22, and the install's own "yes" takes the place of `coai.terminalAccountsInstallMissingTools`. Cited
below: 5 ("left", a dash, never a zero), 7 (O2, full emails in the view), 13 (N1, the writes of Claude Code are
accepted; a disturbed session and unbounded growth are not), 16 (N4, `agy --version` every round), 17 (N5, the
install of `jq`, E6).

### K.22 — *Security*: what an attacker gains

**What an attacker gains** (the list of the stopped plan holds: the appendix, § I, *Security*), with these
changes:

- **A program that opens a terminal named `Creds: evil / x`.** Before the permission: nothing. After it: as F1,
  above. It still cannot push out or undo a "no".
- **Someone who is that Linux user**, and **root in a distro**: neither reaches the extension through a reply,
  and root cannot make the round read another user as root, or a system account. Both word for word: the
  appendix, § K.16.
- **Another Linux user of a distro whose root is read** gains nothing by a file or a folder they can write: as
  UID 0 the script checks the owner and the mode before every start, and a tool that fails the check is not
  started (the rule above). What is left is residual 13.
- **A second window, or a reload, racing a refusal, the Stop or a withdrawal**: the file, as built.
- **Logs**: a failed read logs the place, how it ended and the runner's own last line; a script's stderr goes to
  `/dev/null`; stdout is never logged.

### K.23 — *Security*: the edges

**The edges.** (1) Read-only ssh calls that a TIMER starts, to every host of an open `Creds:` terminal that is not
refused — **now only after one explicit permission**. (2) New: local `wsl.exe` calls by the same timer — an
enumeration, the users script as root, the accounts script as each Linux user, and as root for root's own
place. (3) On a usage read, a start of
Claude Code there, which calls Anthropic with that user's own login (off until E4). (4) In every round, a start of
`agy` for its version. (5) Text from a VM or a distro drawn in a webview. E6 adds a sixth, a call that changes a
machine.

### K.24 to K.33 — ten more passages, moved with entry 4.1 on 2026-11-26

These ten were moved from the plan as it stood at `ec8c7aab`, word for word, in the commit that records the
ledger entry 4.1: the owner's word that the tab's table is grouped by server, by Claude account or by user,
and that every column sorts. The plan needed room for that design, and it stays under 100 KB. As above, none
is a rule of its own: each is a record of how the plan came to epoch 4, the owner's answers as they were
written down, a list of what is retired with every key and file, a summary of rules that § 3 keeps, the
text of residuals of which the plan keeps the number and the statement, a table of which the plan keeps the
sums and the rule, three steps of the build order that are done, a bullet of § 2 that says in short what
§ 4 says in full, the list of what `SECURITY.md` gains, or a record of a pull request that is merged and of
three todo files. The plan says in a line or two what each passage holds. Where the plan of today says
something else, the plan of today holds.

### K.24 — The status block: how the plan came to epoch 4

It stood in the status block, between its first line and "Epoch 4 has no change yet."

> Epic E1 is merged (#1047, `d0241e45`): a read-only round every 10 minutes and a list, with the Claude usage read
> off. The owner's real-window check of its pre-release VSIX 2.0.901 gave the ledger entries 2.2 and 2.3, which
> stopped the plan on 2026-11-25. This text is the revision which that stop ordered (step 4 of *The full stop*,
> `plan-change-control.md`). **The owner approved it as epoch 3 on 2026-11-26**, with three answers that belong to
> the approved baseline and are no ledger entry: root of a WSL distro is read too, as itself (*Owner decisions*,
> 24); Q4 is "yes"; and the go for the measurements W6, W7 and W9, which were run that day. **Epoch 3 then took
> two changes, 3.1 and 3.2** — the edges of a withdrawal, found by the security spec of E2.S1 — **and the second
> stopped it. The owner approved epoch 4 the same day, at once, with NO re-review** (*Re-review for epoch 4* says
> what was not run), and with one more answer, which is baseline: **the login session that WSL opens for a user
> it reads is accepted** (decision 25; § 5).

### K.25 — *Owner decisions*: the owner's answers of 2026-11-25 and of 2026-11-26

They stood after decision 25.

**The owner's answers, 2026-11-25 evening:** Q1 **yes** (one "yes" for everything read from a host, the chat's
session read included); Q2 **not started** (a stopped distro is never started for a read); Q3 **one more one-time
"yes"** for "install `jq` where it is missing", asked once for all hosts; without rights the command is shown. Q4 was
not asked that evening. The owner also gave the go for the cut of *Build order* step 1, over the PR cap.

**The owner's answers, 2026-11-26, with the approval of epoch 3:** root is read too (24); Q4 **yes** — the owner
checks a VSIX built from the head of each of E2, E3 and E4 in a real window BEFORE that epic merges (merge
condition 5); and **the go for the measurements** W9, on this PC, and W6 and W7, on a VM (§ 5).

### K.26 — § 3: what is retired

- **What is retired** (each is in main since #1047 and in no release): the automatic "yes" and the write of it; the
  cap rule that drops an automatic row; the timer's use of a "yes" that was given for a session read; the setting
  `coai.terminalAccountsAllowNewHosts` in the manifest, `SAFETY_KEYS` (`src/modelKeys.ts`), `NEVER_TRANSFERRED`
  (`src/configTransfer.ts`), `src/sideConfig.ts`, the words of `src/remoteHostsSaid.ts` and the help; the row *This workspace sets …*; the daily notice `accounts-hosts-allowed`; the
  first-round notice `accounts-read-every-ten-minutes`; the fields `toldUtc` and `lastAutoNoticeUtc` of the state
  row; the door answers `not-asked` and `full` for a new host; the per-host question and its *Read* button, in
  the road and in the hosts list, which keeps *Not this host* and *Read this host again*. The parser still KNOWS a
  "yes" row and `how: 'auto'`, so a list that holds one is not damaged; neither is an answer.

### K.27 — § 3: what a person can and cannot choose

- **What a person can and cannot choose.** One "yes" for everything read; a "no" for any one host; a stop of the
  round; a withdrawal, which stops both doors. Not: the session read without the permission; a withdrawal on one
  side of the machine only; the accounts read on
  a few hosts only, without refusing each of the others; and not the session read of one host without its
  accounts read while the round reads the other hosts — *Stop the round* stops the accounts read of every host
  and keeps the session read.

### K.28 — *Security*, *Residuals*: the two cases that gate E2-G1 added, and residuals 9 to 13 in full

It stood in the paragraph *Residuals*, after "allows nothing any more."

**The spec of E2.S1 holds the eight as they stand after its story (its
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

### K.29 — § 4, item 4: the time table of revision 2, and the test as worded

It stood in item 4 (T46), after "that second is added:".

   | Mode | The stages, at their latest | Sum | With ssh's 5 s to connect |
   |---|---|---|---|
   | `no-claude-usage` | account 4 s; version 2 + 1 s; the Codex files 1 + 1 + 3 s; `agy` 4 + 1 s | 17 s | **22 s** |
   | `claude-usage` | account 4 s; version 2 + 1 s; usage 9 s | 16 s | **21 s** |

   Both are 8 s or more under the runner's ceiling of 30 s, and no limit is shortened. **The test sums, for each
   mode, every stage's reader limit, one KILL second for each stage that can outlive its reader, and the connect
   time, and asserts the total is at most the ceiling minus 2 s**; and a run with a vendor program that ignores
   TERM ends inside that sum.

### K.30 — *Build order*: steps 1, 2 and 4, which are done

1. **The cut, during the stop** (*The full stop*, step 6: merged work that contradicts the revised plan is
   corrected in a new pull request; the owner gave the go on 2026-11-25). **Done: merged as #1051,
   `b1ec467c`.** Its four parts as they were planned: the appendix, § K.6, word for word.
2. **The owner approves epoch 3**, with the answer to Q4, after the re-review round of this text. **Done,
   2026-11-26:** with Q4 "yes", root read too (decision 24) and the go for W6, W7 and W9.
4. **Alongside E2 and E3, by hand, each with the owner's go:** W9 on this PC — before the spec of E3.S1 is final —
   and W6, W7 and W8 on a VM. **All four were run on 2026-11-26** (§ 5).

### K.31 — § 2: the usage call names its account

It stood before *The round JOINS two replies*; § 4, item 3, holds the rule in full.

- **The usage call names the account it is for, and the SCRIPT checks it** (the plan round's finding 2; § 4). The
  call carries a digest of the account the first reply named. The script reads the account again and runs the
  usage command only when the digest is the same; else it answers *account changed* and runs no usage command.
  So a place that changed to an account already read in this round cannot ask for it a second time.

### K.32 — *Security*: what `SECURITY.md` gains, epic by epic

`SECURITY.md`: the section on the extension's ssh calls is rewritten by E2 — its sentence *"A host is contacted
unless the person refused it"* becomes *"No host is contacted before the person's one permission"* — and gains the
permission, where it is kept and what it covers, the tab and its CSP; by E3 the WSL road, who runs as whom, root's
own place with what is started as root, the login session of a read, and the interop residual; by E4 what W6
and W7 showed.

### K.33 — Two records: the merge condition of the cut, and the todo files

The first stood in *Merge conditions*, the second at the end of *Boundaries with other plans*.

**For the cut of step 1:** conditions 2 to 4 and 6, a test that no runner is called by a tick or by a press, and a
test that the session door asks its question for a host whose only row is an automatic one.

The three todo files say on their side that this revision takes them up and that epoch 3 is approved
(2026-11-26); each is built by the story that closes it.

### K.34 to K.40 — seven more passages, moved with entries 4.2 and 4.3 on 2026-11-26

These seven are from the plan as it stood at `f5f3362b`, word for word, put here in the commit that records
the ledger entries 4.2 and 4.3 and the owner's approval of epoch 5. They are of two kinds.

**K.34 to K.38 are texts that entries 4.2 and 4.3 reworded**, kept as they were approved for epoch 4: the
status block, the build order, three items of the Definition of Done, the merge conditions, and the parts of
*Epics* that say how many epics there are, what each depends on and what E6 is. **They no longer hold where
the plan of today says something else**: the owner chose ONE real-window check on the combined build of E2,
E3 and E4, moved E6 to the next release with its gate R18, set E5's gates to run once, and accepted
residual 9.

**K.39 and K.40 are passages moved for room**, so that the plan stays under 100 KB: the scope and the
stories of E2 and of E3, of which the plan keeps the names and the marks, and the text of two of the five
things that *Security* names and does not prevent, with decision 25 in full, of which the plan keeps the
rule and the statement. They hold as written, but for one sentence that K.39 names.

### K.34 — The status block as approved for epoch 4

It stood on lines 3 to 15 of the plan.

> Status: **In progress, 2026-11-26 — epoch 4 is approved by the owner; E1 and the cut are merged; E2 may start.**
> How the plan came here is in the ledger below: E1 merged (#1047), the stop at 2.2 and 2.3, epoch 3 approved by
> the owner, its stop at 3.1 and 3.2, and **epoch 4 approved the same day with NO re-review** (*Re-review for
> epoch 4* says what was not run). The status as it said so: the appendix, § K.24, word for word.
> **Epoch 4 has one change, 4.1** (the owner: the tab's table is grouped
> by server, by Claude account or by user, and every column sorts); one more stops the plan. The cut that step 6
> of the stop ordered is merged (#1051, `b1ec467c`): no build from main reads a host by itself. **E2 may start**;
> no code of E2 to E6 is written yet. Scope: `src_vs_code` only — the icon in the terminal panel's title bar; ONE permission to read, asked once; a tab with a table — by host, or by Claude account or by user (4.1) — of the Claude Code, Codex and
> Antigravity accounts and their limits; a round every 10 minutes over the hosts of the open
> `Creds: <host> / <session>` terminals, over ssh, and over the Linux users of the WSL distros of this PC, root
> among them. The epics E2 to E5 only read; the install of `jq` (owner decision 17, under a "yes" of its own) is the last epic, E6. No change to
> `src_mcp`, `src_server` or CredsForDevs. The plan as it stood at the stop (138 KB) is in the appendix, § I, word
> for word: [PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_appendix.md](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_appendix.md).

### K.35 — *Build order*: steps 3 to 10 as approved for epoch 4

3. **E2 — the permission and the table.** Its stories by Opus; the security spec of E2.S1 by Fable before its
   code; the code gate; a VSIX from the pull request's head; the owner's rows; merged.
4. **W9 on this PC, and W6, W7 and W8 on a VM, by hand** — all four run on 2026-11-26 (§ 5).
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

### K.36 — *Definition of Done*: three items as approved for epoch 4

In the plan's order: the item on a "no", the Stop and a withdrawal, which entry 4.3 qualifies by residual 9;
the item on the real-window rows, which entry 4.2 changes to one check; and the item on E6, which entry 4.2
takes out of this release.

- [ ] **A "no" for one host, the Stop and a withdrawal always win** — on the side that keeps them, against a
      round, a reload, a second window, a stale write and a damaged file — shown by the file's tests and by R12.

- [ ] The real-window rows of E2, E3 and E4 each have the owner's recorded result before that epic merged; the
      rows of E5 have one before the tag, and R7, R12 and R18 passed on the build that is tagged. W6, W7, W8 and
      W9 are in the two RESULTS files.

- [ ] **E6**, the installer of decision 17, is built by its own stories and spec and passed its own checks:
      nothing is installed before ONE more "yes", asked once for all hosts and withdrawn by a press; where the
      user has no rights the command is shown. No setting for it exists (decision 20).

### K.37 — *Merge conditions* as approved for epoch 4

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
**For the cut of step 1** (merged, #1051): the appendix, § K.33.
**For the release:** E5's rows recorded; **the gates R7, R12 and R18 passed on the head that is tagged** — E6's
build, or E5's when E6 is not in the release; `review_feature` resolved; no extension release from main between #1047 and the cut.

### K.38 — *Epics* as approved for epoch 4: the count and the cornerstone quota, what E2 to E5 depend on, and E6

In the plan's order: the three opening paragraphs of *Epics* with the cornerstone line; *Depends on* of E2;
*Depends on* of E3; *Depends on* of E4; *Depends on* and *Done when* of E5; and the whole text of E6 under its
heading "E6 — The `jq` installer".

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

- **Depends on.** The cut merged; epoch 3 approved. Both hold.

- **Depends on.** E2 merged. **W9 has a recorded result before the spec of E3.S1 is final**: it has one, and
  the owner chose on its one open item: the login session is accepted (decision 25).

- **Depends on.** E3.S2 on main — the script of revision 2 AND the round's join, which that story owns; W6 and W7
  recorded, W7 passed. It does not wait for E3's other stories.

- **Depends on.** E4 merged, or E3 merged and E4 recorded as off.
- **Done when.** Each row has a recorded result; **R7 and R12 are gates**, and both run again on E6's build.

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

### K.39 — *Epics*: the scope and the stories of E2 and of E3

In the plan's order: *Scope* of E2, *Stories* of E2, *Scope* of E3, *Stories* of E3. **One sentence no longer
holds** (entry 4.2): that E3.S2 may merge first, as its own pull request, on merge conditions 1 to 4 and 6.
Every pull request of E2, E3 and E4 merges after the one check.

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

- **Stories** (named here; written by Opus). **E2.S1 — the permission (SECURITY SPEC by Fable, outside the
  quota):** the record in the file, the withdrawal files and their stamps, the one door for the round and for the
  session read, the per-host "yes" and its question retired, the upgrade, T8 and T9. **E2.S2 — the table's model** (pure): the three groupings and the order rule (entry 4.1). **E2.S3 — the tab:** the
  panel, the page run by its tests, the messages, the ages that tick, the clipboard, the switch *Group by* and
  the sorting headers with their state. **E2.S4 — the round under
  the permission.** **E2.S5 — the documents.**

- **Scope.** A NEW module for the WSL road beside `src/remoteSsh.ts` (the absolute `wsl.exe`, the two list calls
  and their decoding, the users call, the accounts call as a user and as root for root's own place, the caps); the users script as a second pinned
  constant; `src/remoteAccountsScript.ts`, `src/remoteAccountsReply.ts` and the S2 spec (revision 2);
  `src/remoteAccountsRound.ts` (places; the usage reply joined to the first); the table's model and the page (the
  `wsl` block); the tests; the documents and `SECURITY.md`.

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
  block of the table, the WSL places in the three groupings (entry 4.1). **E3.S4 — the documents.**

### K.40 — *Security*: root's own programs, the login session, and decision 25 in full

The first two stood in the list "Five things are named, not prevented", as its first and its last item; the
three between them are in § K.19. The third is decision 25 of *Owner decisions*.

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
- **A read of a WSL user opens a login session of that user, by WSL** (decision 25, which says what it is;
  W9b). **So whoever can write a user's profile decides what runs as that user when our timer first reads
  the distro, and root's profile runs as root.** WSL starts it before any line of ours, so the check before
  a start as UID 0 does not reach it (residual 13), and a kill of our call does not end it. It is the profile
  that every login of that user runs; what is new is that our timer brings the login about, with nobody at
  the keyboard. The owner accepted it, and the permission's text says it.

25. **2026-11-26, with the approval of epoch 4: the login session of a WSL read is accepted.** On a distro with
    systemd, the first `-u <user>` call after the distro starts makes WSL open a login session for that user,
    root included: `/bin/login -f`, `systemd --user` and a login shell (`bash` in W9), which runs that user's
    profile (`~/.bash_profile` in W9) — root's as root. A first login makes two empty files in that home
    (`.motd_shown`, `.cache/motd.legal-displayed`), and the session stays until the distro stops. With
    `systemd=false` there is none. The owner was shown it (W9) and chose to accept it, not to read fewer users.
    So a read is named for what it is: the permission's text (§ 3) and *Security* say it.

### K.41 to K.44 — four more passages, put here with the entries of epoch 5 on 2026-11-27

These four were put here in the commit that records the ledger entries 5.1 to 5.9 and the owner's approval of
epoch 6. They are of three kinds.

**K.41 is the ledger entry 5.1 as it was first written** (`5c15d213`, 2026-11-27). It held two of the owner's
answers in one entry. Gate E6-G1 found that they are independent causes, and `plan-change-control.md` counts
one entry for each. The commit of this passage wrote them as entries 5.1 and 5.2, so the count went up, not
down. The first wording is kept here, word for word.

**K.42 is texts that the entries of epoch 5 reworded**, kept as they were approved for epoch 5 (the plan at
`2b67c9c9`, #1052): the status block, one item of *What this plan does NOT do*, the sentence on E6 under
*Build order*, two items of the Definition of Done, *For the release* of the merge conditions, the opening
of *Epics* and the cornerstone line, and the text of E6. **They no longer hold where the plan of today says
something else**: E6 is the sixth epic of the plan again, and extension 2.1.0 was released before R12 and the
VM half of R7 had a result.

**K.43 and K.44 are passages moved for room**, so that the plan stays under 100 KB: the new tests of the
test plan, and the sections of E2, E3 and E4 in *Epics* with their real-window rows. Both are of the three
epics that extension 2.1.0 holds. They hold as written — but entry 7.1 narrows K.43's root test: as UID 0 a `claude`, `codex` or `agy` in a folder
another user can write is started, with a warning that names it. The plan keeps the names of what is tested, the goal
of each epic and the number of each row, and cites these sections. The tests of E6 are in its own document,
§ 6.

### K.41 — The ledger: entry 5.1 as it was first written

It stood under the line of epoch 5, as that epoch's only entry.

```text
- 5.1 · 2026-11-27 · owner · epics, merge-conditions — E6 is the sixth epic of this plan again, its spec the third cornerstone; R12 and the VM half of R7, run on E6's build, stand for 2.1.0 too
  (Extension 2.1.0, with E2 to E4, was released on 2026-11-27. The owner answered the questions of E6's
  document, its § 11, the same day, in one answer: every recommendation is taken. Two of them change this
  plan. O1: E6 is the sixth epic of THIS plan, not a plan of its own; the cornerstone quota is counted again
  for six epics, Q = ⌈2·6/5⌉ = 3, and E6.S1 takes the third mark, for area (f), as § K.38 planned. The plan
  stays in `todo/` until E6 ships. O5: R12 and the VM half of R7 have no result on 2.1.0; their runs on E6's
  build stand for 2.1.0 as well, and the E5 results say that 2.1.0 was released before them. The answers O2,
  O3, O4 and O6 are E6's own and are recorded in its document. The first change of epoch 5: one more stops the
  plan.)
```

### K.42 — Texts as approved for epoch 5, which the entries of epoch 5 reworded

In the plan's order: the status block, which stood on lines 3 to 15; the item on an install of *What this
plan does NOT do*; the sentence on E6 at the end of *Build order*; the item on the real-window rows and the
item on an install of the *Definition of Done*; *For the release* of the *Merge conditions*; the two opening
paragraphs of *Epics* and the cornerstone line; and the text of E6, which stood under the heading "E6 — The
`jq` installer · MOVED to the next release (entry 4.2)".

> Status: **In progress, 2026-11-26 — epoch 5 is approved by the owner; E1 and the cut are merged; E2 is being built.**
> How the plan came here is in the ledger below; the status as it said so before: the appendix, § K.24 and
> § K.34. **Epoch 4 took three changes** — 4.1 (the table is grouped and sorted), 4.2 (the release in 24
> hours: ONE real-window check on the combined build of E2 to E4, E6 to the next release, E5's gates once)
> and 4.3 (residual 9 is accepted) — **and the owner approved epoch 5 the same day with NO re-review**
> (*Re-review for epoch 5*). **Epoch 5 has no change yet**; its second one stops the plan. The cut is merged
> (#1051, `b1ec467c`). E2's stories and spec are written and its S1 is in code
> (`feat/terminal-accounts-e2`); nothing of E2 to E4 is merged. Scope: `src_vs_code` only — the icon in the terminal panel's title bar; ONE permission to read, asked once; a tab with a table — by host, or by Claude account or by user (4.1) — of the Claude Code, Codex and
> Antigravity accounts and their limits; a round every 10 minutes over the hosts of the open
> `Creds: <host> / <session>` terminals, over ssh, and over the Linux users of the WSL distros of this PC, root
> among them. This release only reads: where `jq` is missing the tab shows the install command with a copy button, and the installer (decision 17, E6) is the next release's (4.2). No change to
> `src_mcp`, `src_server` or CredsForDevs. The plan as it stood at the stop (138 KB) is in the appendix, § I, word
> for word: [PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_appendix.md](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_appendix.md).

- **No install in this release** (E6 is the next release's, entry 4.2). Nothing is written or installed on a
  VM or in a distro by a script of ours.

**E6 — the installer — is not in this order: it is the next release's** (entry 4.2), with its gate R18.

- [ ] The real-window rows of E2, E3 and E4 have the owner's recorded result from ONE check, on a VSIX of the
      combined build, before any of the three merged (entry 4.2); the rows of E5 have one before the tag, and
      R7 and R12 passed, once, on the build that is tagged. W6, W7, W8 and W9 are in the two RESULTS files.

- [ ] **This release installs nothing** (entry 4.2): a place without `jq` shows the install command with a
      copy button, and no more. E6, with R18, is the next release's.

**For the release:** E5's rows recorded; **the gates R7 and R12 passed, once, on the head that is tagged** —
the build of E2 to E4 (R18 goes with E6, to the next release); `review_feature` resolved; no extension release
from main between #1047 and the cut.

**Five epics in this release; one is done. The sixth, E6, moved to the next release (entry 4.2).**
The split is Fable's, after both consultations of the stop (`78e6d44b`, `7315feb0`).
The consultants' rule of 2026-11-19 holds for each, and it is why the split is as it is (the appendix, § K.3).
The rows keep the numbers of the stopped plan where their
meaning is kept (the appendix, § I, *E2*); R11, the row of the retired setting, is gone; R13 to R17 are new.

Stories documents are written by Opus when an epic starts:
`todo/PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E<n>.md`. Cornerstone quota, counted again
for five epics (entry 4.2): Q = ⌈2·E/5⌉ = ⌈2·5/5⌉ = 2. The cornerstone areas Fable names, each with its
reason in the appendix, § K.14: **(a)** what a
script touches there and what it prints; **(b)** the reply's frame and the parser; **(c)** who is contacted and
when; **(d)** the different results of a read, and a missing number that is never a zero; **(e)** who runs as
whom on this PC; **(f)** a change of a machine (E6, so the next release's). Areas (c) and (e) are
authorisation: their specs are Fable's by the security row, outside the quota. Planned mark, made by its
stories document: E3.S2 for (a) and (b).

Cornerstones: 2 of 2 — E1.S2, E3.S2

**Not built in this release.** The owner, 2026-11-26: the installer of decision 17, its one-time "yes" (Q3)
and its gate R18 go to the next release, under a later plan or epic of its own. This release shows the
install command with a copy button (§ 1); R10 of E5 stays as its input. The epic as approved for epoch 4:
the appendix, § K.38; its design: § E.

### K.43 — *Test plan*: the new tests of E2 to E4

It stood in *Test plan*, after the words "of the QuickPick's rows and its host scenario. New:".

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
  word, and **a test fails if any text of a reply is part of it**. **The three groupings (entry 4.1)**, from
  one set of readings: *Claude account* — two places of one account are one block with its usage once; an
  account that is not known is a block of its own; a failed place stays under its older account; *Codex* and *Antigravity* join nothing; a place with no
  Claude account, a refused and a never-read one are in the last block with their sentence; *User* — a row
  for each place; **and a count: every place and every tool reading of the input is in each grouping.** **The
  order rule**: each key both ways; a dash last in both, never a zero; equal keys in name order; an unknown
  grouping or column gives *Server* by name.
- **The page, by RUNNING it** (`bundledPage.test.ts`, the DOM shim; no assertion over page source for behaviour).
  A model whose values hold `<img src=x onerror=…>`, `$(account)` and 10 KB of text: every one is the text of a
  node, and no element exists that the model did not order. Each button posts its message and nothing else.
  **The owner's observation as a test:** the clock of the shim goes on 10 minutes with no message from the host,
  and the heading reads *read 10 min ago*; the clock time on it does not change. A place's update changes that
  block only. A message the host does not know is dropped; `copy-install` for a place with no manager word copies
  nothing. The CSP and the nonce are held by a source assertion, which is legitimate there. **The switch and
  the headers (entry 4.1):** each grouping is drawn, and *Server* again gives the first tree; a header click
  sorts and a second reverses, the dash last both times; neither posts a message; the webview state holds both
  and a page built again reads them; the hostile values are text in each grouping; a place's update keeps both.
- **Host scenario** (`npm run test:host`): the icon opens the tab; a second press reveals the same tab; with the
  call replaced through the test door and the permission given through the keeper, two terminals of two hosts are
  one call each, and the tab's model holds both.
- **The manifest and the help**: the command and its menu entry; the retired setting is gone from the manifest,
  `SAFETY_KEYS` and the help of five languages; `notification-sites.json` generated again; the log scan covers the
  new modules.

### K.44 — *Epics*: E2, E3 and E4 in full, with their real-window rows

Moved with K.43, for room, word for word: the three sections of *Epics* from the heading of E2 to the last
row of E4 — for each epic its goal, scope, what it depends on, its stories and, under *Done when*, the table
of its real-window rows. Only the three headings were put one level down. Extension 2.1.0 holds the three
epics, so this text is the record of what was planned and of what its one real-window check ran. The plan
keeps the goal of each epic and the number of each row. E5, with its rows R7, R12, R10 and W8, stays in the
plan.

#### E2 — One permission, and a table that can be read

- **Goal.** After one "yes" the owner reads, in a tab, every ssh host of the open terminals: its tools, their
  accounts and the limits that this build reads. Before that "yes" nothing is contacted.
- **Scope.** The permission and the one door, the tab with its model and page, the round under the
  permission, the setting out, the help, the tests and the documents: every file, in the appendix, § K.39.
- **Depends on.** The cut merged; epoch 3 approved. Both hold. It merges after the ONE check (merge
  condition 5).
- **Stories** (by Opus; its stories document is written). E2.S1 — the permission (SECURITY SPEC by Fable,
  outside the quota); E2.S2 — the table's model; E2.S3 — the tab; E2.S4 — the round under the permission;
  E2.S5 — the documents. What each holds: § K.39.
- **Done when.** The merge conditions hold, with these rows in the owner's window, on the owner's ssh hosts:

| # | What is run | Pass |
|---|---|---|
| R1 | The icon | It sits beside *Ask other AI* and opens ONE tab; a second press shows the same tab; Ctrl+F finds a host; with no `Creds:` terminal the tab says so |
| R14 | **The permission.** The profile holds a person's "yes", the automatic rows of 2.0.901 and one *Not this host*; the owner's terminals are open | Before the permission: **0** ssh logins on any host in 20 minutes (the VMs' ssh logs). The question is asked once. After *Allow reading*: the first login within 60 s; and when the toolbar says the round has ended — one host at a time, up to 30 s each — exactly 1 login on each host that is not refused, plus R2's one usage login for each Claude account, and 0 on the refused one. A chat door pressed before the permission, on the host with the "yes": the question, and 0 logins. A terminal of a new host, opened later: read in the next round, no question. *Withdraw*: 0 logins in 20 minutes, by the round or by a chat door, also after a reload |
| R15 | **The table.** At least 3 hosts, one of them with a Codex record | For each host the owner reads, without help: the host, its tools, each account, the two windows with "% left" and the reset. The Codex figure equals the record on that VM. A Claude account says why its usage is not read. **The tab stays open 12 minutes untouched: the age of each read grows and matches the clock time beside it.** A hover shows the exact time. A narrow tab stacks the fields. **Entry 4.1:** *Group by* is switched to *Claude account*, to *User* and back to *Server*: each shows every host and account, and an account on 2 hosts has its usage once, both hosts under it. A sort by "% left" with a dash present: the dash is last, both ways. Each grouping stays readable in a narrow tab |
| R5 | A host that cannot be reached | Its failure in full under its heading within 30 s; the other hosts are read in that round |
| R6 | Stopping | Every `Creds:` terminal closed: 0 logins in the next 20 minutes. *Stop the round*: 0 logins in 20 minutes, also after a reload; started again, a round follows within 60 s |
| R8 | The session door | *Take the question* works with the permission and asks no per-host question. *Not this host* on a heading: 0 logins to that host in 20 minutes, also after a reload, and the chat door there refuses. *Read this host again*: read in the next round |

#### E3 — WSL on this PC, and revision 2 of the script

- **Goal.** A `Creds: wsl / …` terminal shows every running distro of this PC and every Linux user in it, root
  among them, each read as that user — root as root; and the script that every place runs is the one the usage
  read can be turned on with.
- **Scope.** A new module for the WSL road, the users script, revision 2 of the accounts script with its
  reply and the S2 spec, places in the round, the `wsl` block of the table: every file, in § K.39.
- **Depends on.** E2 — merged, or under it in the same pull request or branch (entry 4.2). **W9 has a
  recorded result before the spec of E3.S1 is final**: it has one (decision 25).
- **Stories.** E3.S1 — the WSL road (SECURITY SPEC by Fable, outside the quota); E3.S2 — the scripts,
  revision 2, with the round's join (CORNERSTONE, `model: fable`; one pull request, never two; no row of
  its own); E3.S3 — places; E3.S4 — the documents. What each holds: § K.39 — but the early merge of E3.S2
  is gone (entry 4.2).
- **Done when.** The merge conditions hold, with these rows in the owner's window on this PC:

| # | What is run | Pass |
|---|---|---|
| R13 | **Two WSL users, and root.** A distro with two Linux users, each signed in to a tool, and a distro whose root is signed in to a tool; a `Creds: wsl / …` terminal | Both users are under `wsl`, each with its own tools and accounts, and each account is the one that user is signed in to. **Root is listed under its distro, read as root, with the account root is signed in to**; in a distro where root has no tool there is no line for root. A second running distro is listed with its users; one that is not running says so and stays stopped. No ssh login to a host `wsl` is tried. Each of them is a place of its own in *Claude account* and in *User* (entry 4.1) |
| R16 | **A known sample** (the consultants' criterion) | One figure whose value the owner knows from the tool itself — a Codex window, or a Claude window once E4 is in — shows that value; a window with no reading says why; the age of the read stays true while the tab is open |
| R17 | *Not this host* on `wsl` | No `wsl.exe` call of this window in 20 minutes, watched on the PC by the method the stories document fixes; also after a reload |
| R9a | The time of a read, 10 calls for each place, ssh and WSL | None is killed at 30 s; the slowest is under 20 s; the medians are recorded |

#### E4 — The Claude usage, on

- **Goal.** The Claude windows show numbers.
- **Scope.** ONE constant, `USAGE_POLICY`, set to the row W6 selected, with the tests that hold it; the sentences
  of `SECURITY.md` on W6 and W7. A result of W6 or W7 that needs another flag or another script line is a change
  of this plan.
- **Depends on.** E3.S2 — the script of revision 2 AND the round's join, which that story owns — on main or in
  the same pull request: E4 may be in E3's pull request (entry 4.2). W6 and W7 recorded, W7 passed: both hold.
- **Done when.** The merge conditions hold, with these rows:

| # | What is run | Pass |
|---|---|---|
| R3 | The numbers, on at least 3 places | Each Claude percent equals `/usage` run there in the same minute, and the mod line within 1 point, with the same dashes; an account with no 5-hour window shows `—` there |
| R4 | Two places of one account, 6 rounds | Both say *shared across 2 hosts* with the same figures; in each round that reads usage under the policy, `policy-limits.json.stamp.json` changes on exactly one of the two |
| R2 | The logins of a round | Exactly 1 on each host, and 1 more on ONE host of each Claude account and on no other; 0 on a refused host |
| R9b | The time of the usage call, 10 calls | None is killed at 30 s; the slowest is under 20 s |

### K.45 to K.55 — eleven more passages, put here with entries 6.2 and 6.3 on 2026-11-27

These eleven were put here in the commit that records the ledger entries 6.2 and 6.3 and the owner's
approval of epoch 7. Each is taken from the plan as it stood at `dab8a8bd`, word for word. They are of two
kinds.

**K.45 is texts that entries 6.2 and 6.3 reworded**, kept as they were approved for epoch 6: the status
block, and four texts that said what the plan does not read of agy and of Codex. **They no longer hold
where the plan of today says something else**: the tab is to show agy's quota and its account (entry 6.2),
and Codex's account, plan and limits as `codex app-server` states them (entry 6.3).

**K.46 to K.55 are passages moved for room**, so that the plan stays under 100 KB with the two entries and
the record for epoch 7. They hold as written, but for the two places that K.48 and K.55 name, and the plan
keeps a line or two of each and cites these sections. K.46 is what is E6's while that epic is parked
(entry 6.1). K.47 is the rows of E5. K.50 and K.54 are records: of the four measurements, and of the
owner's answers. K.48, K.49, K.51, K.52, K.53 and K.55 are rules of the design that extension 2.1.0 holds.

### K.45 — Texts as approved for epoch 6, which entries 6.2 and 6.3 reworded

In the plan's order: the status block, which stood on lines 3 to 17; in § 1, the sub-bullet *Account* and
the sub-bullet *Usage that was not read*; the item on Codex and agy of *What this plan does NOT do*; and
the paragraph *The edges* of *Security*.

> Status: **In progress, 2026-11-27 — epoch 6 is approved by the owner; E1 to E4 are released as extension 2.1.0; of E5, the gate R12 is not run; E6, the `jq` installer, is parked (6.1).**
> How the plan came here is in the ledger below; the status as it said so before: the appendix, § K.24,
> § K.34 and § K.42. **Epoch 5 took the changes 5.1 to 5.9 on 2026-11-27** — the owner's four answers to
> E6's questions (5.1: E6 is the sixth epic of this plan again; 5.2: 2.1.0 was released before R12, whose
> run on E6's build stands for it; 5.3: E6's build order, with a gate before its S3; 5.4: *Withdraw the
> permission* withdraws the install "yes" too) and five places where E6's spec leaves § E (5.5 to 5.9) —
> **and the owner approved epoch 6 the same day with NO re-review** (*Re-review for epoch 6*). **Epoch 6
> has no change yet**; its second one stops the plan. Extension 2.1.0 (2026-11-27; #1055, #1057) holds E2
> to E4; 2.1.1 is a wording fix. E5: R7 passed in WSL and on the VM, R10 is recorded, R12 is open. E6's
> stories and spec are written and gate E6-G1 read them; no code of E6 exists. Scope: `src_vs_code` only — the icon in the terminal panel's title bar; ONE permission to read, asked once; a tab with a table — by host, or by Claude account or by user (4.1) — of the Claude Code, Codex and
> Antigravity accounts and their limits; a round every 10 minutes over the hosts of the open
> `Creds: <host> / <session>` terminals, over ssh, and over the Linux users of the WSL distros of this PC, root
> among them. **2.1.0 only reads**: where `jq` is missing the tab shows the install command with a copy button. **E6, the next release, installs `jq` after one more "yes"** (decision 17; entries 4.2 and 5.1). No change to
> `src_mcp`, `src_server` or CredsForDevs. The plan as it stood at the stop (138 KB) is in the appendix, § I, word
> for word: [PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_appendix.md](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_appendix.md).

  - *Account:* for Claude Code the full email and the organization (decision 7); under it, muted, the plan; and
    *shared across N hosts* when N places of this window answer with the same account — each of them then shows
    the SAME figures, those of the one usage read (§ 2). Two places are one account only when the vendor and the
    whole account text are equal; an account that is not known is never shared. Codex states no account name, so
    the cell says so and shows the plan; **the age of the Codex session file is on Codex's own row**. agy: *its
    account is not read*.

  - *Usage that was not read* spans both window cells and says why: *Usage not read: CoAI does not ask Claude Code
    for usage in this build*; *Usage unavailable: Claude Code 2.1.326 printed no usage line this version
    understands*; *agy states no usage*. It is never a row of dashes with no reason.

- No live Codex read, no `codex login status`, no opening of `~/.codex/auth.json`; no start of `agy` but with its
  version flag; no opening of `~/.claude.json` or of a credential file by our script. **No road (b).**

**The edges** are five, and E6 adds a sixth (word for word: the appendix, § K.23): ssh calls by a timer, now
only after the permission; local `wsl.exe` calls by it — **with the login session that WSL opens for a user
that is read**; a start of Claude Code on a usage read; a start of `agy` every round; VM text in a webview.

### K.46 — E6 while it is parked: its steps of *Build order*, its merge conditions, its section of *Epics*

Three passages. The first stood in *Build order*, after the paragraph "Where this order stands,
2026-11-27". The second stood at the end of *Merge conditions*. The third stood in *Epics*, under the
heading "E6 — The `jq` installer · PARKED (entry 6.1)".

**E6 — the installer — follows, as the next release** (entries 4.2, 5.1 and 5.3), with its gate R18:

11. Gate E6-G1 and E6's plan round, both before any code. The gate ran on 2026-11-27.
12. **E6.S1 and E6.S2**: the script with its container run, and the install "yes" with the record. They
    touch no machine.
13. **A gate before any code of E6.S3** (entry 5.3): the rows of R10 and the fact of R10 (f) are on record,
    and they name a place of the owner that would get an install — no `jq`, Claude Code or Codex there, a
    login user who is root or has `sudo` without a password. With no such place the work stops here, and
    the owner is asked.
14. **E6.S3, S4 and S5**; the code gate; the container run green on the head that merges.
15. The owner's rows on ONE VSIX of E6's build; **R18, R12 and R7 are gates** there. Then the merge.
16. `review_feature` over the plan's epics; the extension release of E6, its tag pushed alone and its run
    observed; `POST_DEPLOY.md` run; the plan promoted, as step 10 says.

**For the pull request of E6 — one, or two** (entries 5.1 and 5.3):

1. The parent is not stopped. Gate E6-G1 ran and E6's plan round passed on its document, before any code;
   its spec is Fable's.
2. **Before any code of E6.S3, the gate of entry 5.3 has a recorded answer**: a place of the owner would get
   an install, or the owner said to go on.
3. Every test of E6's document, § 6, is green; each "never" test was seen RED once with its message; **the
   container run is green on the head that merges, with its job present.**
4. The code gate passed (`review_code`, fed the scope), the own Opus review ran, and every finding is
   resolved. CI is green on the head that merges, with its checks present.
5. **The owner's rows of E6's document, § 8, have a recorded result on ONE VSIX of E6's build; R18, R12 and
   R7 passed on it.**
6. `SECURITY.md` and the help say what the head does.

**For the release of E6:** `review_feature` resolved; the tag pushed alone and its run observed.

**Not in 2.1.0, which shows the install command with a copy button (§ 1). It is the sixth epic of THIS
plan, for the release after 2.1.0** (the owner, 2026-11-27: entry 5.1; entry 4.2 had said "under a later
plan or epic of its own"). Plan only: no code of it exists.

- **Goal.** Decision 17: where `jq` is missing and needed, it is installed when the login user may; where
  the user may not, the tab shows the command and says why.
- **The consent (Q3).** One more "yes", asked ONCE for every place, by a person's press; kept in the locked
  file of the side beside the permission to read; never given by that permission; withdrawn by a press of
  its own, **and by *Withdraw the permission* too** (entry 5.4). No setting.
- **Scope, stories and spec.** ONE document:
  [PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E6.md](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E6.md).
  Five stories: E6.S1 — the script and its reply (CORNERSTONE for area (f), `model: fable`); E6.S2 — the
  install "yes" and the record, and E6.S3 — the install in the round (SECURITY SPECS by Fable, outside the
  quota); E6.S4 — the tab; E6.S5 — the documents. The design it starts from: the appendix, § E and § K.38.
  Where it leaves § E: entries 5.5 to 5.9. The owner's answers to its six questions: its § 11.
- **Depends on.** E5's R10: recorded. **S1 and S2 may start; before any code of S3 a gate — a place of the
  owner would get an install, or the work stops and the owner is asked** (entry 5.3). **As measured on
  2026-11-27 there is no such place**: every measured place has `jq` now, and no measured login user has
  `sudo` without a password.
- **Done when.** The merge conditions for E6 hold, with the rows of its document, § 8.

### K.47 — *Epics*: the rows of E5

It stood at the end of the section "E5 — The long runs before the release".

| # | What is run | Pass |
|---|---|---|
| R7 | At least two hours and until 12 rounds have ended, on a VM AND in a distro, each WITH live Claude Code, Codex or agy sessions | **Gate. 0** live sessions that ask to sign in again, lose their settings or MCP servers, or stop or restart. The time and size of the agy program before and after: recorded |
| R12 | **Two windows.** (a) *Not this host* in window A while B's round runs. (b) *Not this host* in A; A is closed at once; B goes on for three rounds and is made to write `globalState`; both are reloaded. (c) The same with *Stop the round*. (d) The same with *Withdraw the permission*, and then a chat door is pressed in B | **Gate.** (a) At most 1 login to that host after the press, 0 from then on. (b) 0 logins to that host from the press on. (c) and (d) 0 logins to any host from the press on. In each the folder is read back and holds the change. A lost "no", Stop or withdrawal fails the gate: the build is not released, and the cure is a change of this plan |
| R10 | Which places lack `jq` | A table: `jq` there or not, the manager word, root or `sudo` without a password. A place without `jq` shows the fixed command; after the owner runs it, the next round reads the accounts. The count is the input of E6 |
| W8 | A start of `agy` (§ 5) | A record |

### K.48 — § 1: the grouping and the sort (entry 4.1)

It stood in § 1, after the sub-bullet *Usage that was not read*. **One reason in it no longer holds.** Of
the sections *Codex* and *Antigravity* it says "their accounts cannot be told apart, so none is joined".
Since entries 6.2 and 6.3 a row of those sections shows its account. They stay sections of their own.

- **Group by: Server | Claude account | User** (the owner, entry 4.1): a switch in the toolbar, default
  *Server*. Each grouping shows the SAME readings in real tables (`<table>`, a header row); none hides a place,
  a tool, an account or a failure.
  - *Server:* the blocks and rows above.
  - *Claude account:* one block for each Claude account, headed by the account text as read; one that cannot
    be told apart is a block of its own (the rule of *shared across N*). Its usage — the two windows, or the
    sentence why they were not read — stands ONCE, on the block: it is the account's, not a place's. Under it,
    a row for each place where it is signed in (*host-a*, *Ubuntu · user-a*) with the age of that read; a place
    whose last read failed stays under the account of its older read, with the failure and that read's time.
    Then two sections, *Codex* and *Antigravity*, a row for each place that has the tool: their accounts
    cannot be told apart, so none is joined. Last, *no Claude account*: every place that is in no Claude
    block, with what its heading says.
  - *User:* ONE table, a row for each place, a column for each tool with its account and its windows. A WSL
    place is *distro · user*; an ssh host is one Linux user of a VM and is named by its host — the script
    prints no user name, and this entry adds none.
- **Every column header sorts**: one click ascending, a second descending, and the header shows which
  (`aria-sort`). The keys: name, account, % left, reset time, age of the read. Rows are sorted inside their
  block, and blocks by their own value of the key where they have one (a host's name and age; an account's
  text, % left and reset), else by their first row. **A dash — a missing number — sorts LAST in both
  directions, never as a zero** (decision 5). Equal keys keep the name order; the sections keep their places.
- **The grouping and the sort are of this tab only**: in the page's own webview state, and in no message,
  file, `globalState` key, setting or log. A state that names an unknown grouping or column is read as
  *Server*, by name. **The three groupings and the order rule are ONE pure module, tested without a browser**;
  the stories document decides whether it runs in the host, which then posts all three, or in the page,
  bundled as the word rules are.

### K.49 — § 3: the upgrade table

It stood in § 3, after the bullet *What is retired*.

- **The upgrade starts with NO permission, whatever is stored.**

  | Stored before | After the upgrade |
  |---|---|
  | A person's "yes" for a host (from 0.87.0 on) | **Not honoured.** The first press of a chat door asks the one question and reads nothing; after *Allow reading* every host that is not refused is read, by both doors. This changes a released behaviour, and the release notes say so |
  | An automatic "yes" (only the pre-release 2.0.901 wrote one) | No answer. Nothing is read |
  | A "no", in the file or as a row | A refusal, as before, on every side that reads it |
  | The Stop | Stays. A permission given later does not start a stopped round |
  | `coai.terminalAccountsAllowNewHosts` in a settings file | Not read |

### K.50 — § 5: the four measurements, as the section said it

It stood at the head of § 5, "What is still to be measured", before the paragraph "Within the plan as
written".

Each needs the owner's go for its exact commands. **W9, W6 and W7 have the owner's go (2026-11-26); W8 had none and ran
with W7; the owner was told.** **W-numbers here are measurements** (the S1 spec uses W-numbers for its sentences). **Run 2026-11-26:** W7
passed (it started Claude Code's own `rg` and `ps` call; no hook, MCP server or plugin of the person),
W6 → every round (retirement seen), W8 recorded:
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

### K.51 — § 2: the three steps of *Which places*

It stood in § 2, in the bullet *Which places*, after the words "the round enumerates, in every round:" and
before the words "The caps".

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

### K.52 — § 3: a withdrawal is a file of its own, and the rules of its stamp

It stood in § 3, after the bullet *The permission*.

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

### K.53 — § 1: three rules of the page

It stood in § 1, after the bullet on the page without the permission.

- **Each place is drawn when its call lands**, not at the end of the round: the host posts that place's part of the
  model, and the page changes that block only — in the other groupings the rows of that place and the blocks
  that hold them — so a reader's scroll place and the find bar stay, and so do the grouping and the sort.
- **Times stay true while the tab is open.** The model carries every time as an absolute instant. The page writes
  the ages and the *resets in* texts itself and writes them again every 15 s from its own clock, so an age grows
  with no message from the host. The clock time of the last read is always on the heading; a hover over an age or
  a reset shows the exact local date and time. The pure word rules (`usageWords.ts`, `agoWords`) are bundled into
  the page; there is one copy of them.
- **Narrow widths**: under a width fixed in the spec the columns stack, each field with its label, in every
  grouping; the switch and a way to choose the sort stay on the page.

### K.54 — *Owner decisions*: the short list of the owner's answers

It stood in *Owner decisions*, after decision 26.

**The owner's answers** of 2026-11-25 evening and of 2026-11-26, word for word: the appendix, § K.25. In short:
Q1 **yes** — one "yes" for everything read from a host, the chat's session read included; Q2 a stopped distro
is **not started**; Q3 **one more one-time "yes"** for the install of `jq`; Q4 **yes** — merge condition 5;
root is read (24); the go for the cut of *Build order* step 1 and for W6, W7 and W9. **Later that day, for
a release in 24 hours** (entries 4.2 and 4.3): Q4 is ONE check, on the combined build of E2 to E4; E6 moves
to the next release; E5's gates run once; residual 9 is accepted.

### K.55 — § 4: the four changes of revision 2, and the users script

It stood in § 4, after the words "(E3.S2, the cornerstone):" and before the paragraph *The operands*.
**Revision 3 (entries 6.2 and 6.3) changes two sentences of its item 2**: the first call of a place reads
no Codex file any more, and it starts `agy` twice, for its version and for its quota. The usage mode still
starts neither.

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
   second is added: 17 s and 16 s for the two modes, 22 s and 21 s with ssh's 5 s to connect — 8 s or more
   under the ceiling of 30 s, no limit shortened — and a test holds each sum to at most the ceiling minus 2 s
   (the test plan). The table, stage by stage, and the test as worded: the appendix, § K.29.
   What was not taken: the appendix, § K.4.

**The users script** (new; E3.S1, the security spec): a second fixed text with its own sha256, in the same shape
— one function, called only at its end, a begin line `COAI-USERS 1` and an end line. It opens `/etc/passwd` and no
other file, starts no program, and prints for each entry that § 2 names one line with the user's name and UID. A
name counts, there and on this side, only when it fits `[a-z_][a-z0-9_-]{0,31}`; at most 16 lines. A UID counts,
there and on this side, only when it is from 1000 to 59 999 under a name that is not `root`, **or 0 under the
name `root`** (decision 24). Never printed and, if a reply holds one, never read: a UID from 1 to 999; UID 0
under another name; the name `root` with another UID. It has no operand.

### K.56 to K.59 — four more passages, moved with entry 7.1 on 2026-11-27

These four were moved here in the commit that records the ledger entry 7.1: the owner's word that, as
root, a vendor program whose folder another user can write is run all the same, with a warning in its row.
Each is taken from the plan as it stood at `abd770b1`, word for word, so that the plan stays under 100 KB.
They hold as written, and the plan keeps a line or two of each. K.56 and K.57 are of what is merged and
released; K.58 and K.59 are rules of the design that extension 2.1.0 holds.

**What entry 7.1 itself changes is not here.** The words it narrows stay in the plan, each with a sentence
"Since entry 7.1" beside it. **It also narrows what two sections of this appendix say in full**: § K.40,
on root's own programs, and item 1 of § K.55. There, "as UID 0 the script checks, before it starts a
program" still says what is checked. Since entry 7.1 a check that fails stops a basic tool only —
`timeout`, `tail`, `sed`, `grep`, `sleep`, `sha256sum`, `jq`; a vendor program — `claude`, `codex`, `agy` —
is started, and its row warns and names the folder.

**One scope note on K.56.** "The VM half of R7 passed after it" means: on processes and files. The
sign-in state was not read again (`research/RESULTS_accounts_e5_long_runs.md`, R7).

### K.56 — *Merge conditions*: for the pull request of E2, E3 and E4, for E4, for the cut and for the release 2.1.0

It stood at the head of *Merge conditions*. The pull request is merged (#1055), and so is the cut (#1051);
extension 2.1.0 is released.

**For the pull request of the code epics E2, E3 and E4 — one for the three, or several** (entry 4.2):

1. The parent is not stopped, and each epic's stories document is written; a cornerstone or security spec is
   written by Fable before its code.
2. Every row of the test plan that belongs to the epic is green; each leak test and each "never" test of the epic
   was seen RED once with its message.
3. The code gate passed (`review_code`, fed the scope), the own Opus review ran, and every finding is resolved.
4. CI is green on the head that merges, with its checks present.
5. **ONE real-window check, on the combined build: the owner's rows of E2, E3 and E4 have a recorded result on
   ONE VSIX, built from a head that holds the code of all three** (Q4 as changed on 2026-11-26). No pull
   request of the three merges before it. A row that fails is fixed and run again on the new combined head.
6. `SECURITY.md` and the help say what the head does.

**For E4 also:** W6 and W7 are recorded, W7 passed, and revision 2 of the script is on main or in the same
pull request.
**For the cut of step 1** (merged, #1051): the appendix, § K.33.
**For the release 2.1.0:** E5's rows recorded; **the gates R7 and R12 passed, once, on the head that is tagged** —
the build of E2 to E4 (R18 goes with E6, to the next release); `review_feature` resolved; no extension release
from main between #1047 and the cut. **This did not hold as written: 2.1.0 was released on 2026-11-27 before
R12 and the VM half of R7 had a result. The VM half of R7 passed after it; R12 is a gate of E6's build, and
its result there stands for 2.1.0 (entry 5.2).**

### K.57 — *Build order*: steps 1 to 10

It stood at the head of *Build order*, before the paragraph "Where this order stands, 2026-11-27".

1. **The cut** — done: merged as #1051, `b1ec467c`. (Steps 1, 2 and 4 as written: the appendix, § K.30.)
2. **The owner approves epoch 3** — done, 2026-11-26.
3. **E2 — the permission and the table.** Its stories by Opus; the security spec of E2.S1 by Fable before its
   code; the code gate. Not merged before step 7.
4. **W9 on this PC, and W6, W7 and W8 on a VM, by hand** — all four run on 2026-11-26 (§ 5).
5. **E3 — WSL on this PC, and revision 2 of the script**, on top of E2: in E2's pull request or in its own.
6. **E4 — the Claude usage on**, with revision 2: W7 passed and W6 selected the policy (§ 5). It may be in
   E3's pull request.
7. **ONE real-window check, on the combined build** (entry 4.2): a VSIX from a head that holds E2, E3 and E4;
   the owner runs the rows of the three on it, once. Then the pull request, or the several, merge.
8. **E5 — the long runs, once, on the release build** of E2 to E4. No product code.
9. `review_feature` over the built epics, every finding resolved.
10. The extension release, one for the whole plan: the tag pushed alone and its run observed; `POST_DEPLOY.md`
    run; the plan promoted with its deviations, its appendix and its stories documents.

### K.58 — § 2: *Refresh now*, the size of a round, the summary, the one notice

It stood in § 2, after the bullet *The usage policy*.

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

### K.59 — § 3: T9 and T8, taken up

It stood in § 3, after the table *Who may be contacted* and the paragraph on a per-host "yes".

- **T9, taken up: the session road asks that function before EVERY spawn of a press**, not once. A later spawn of
  a press never asks a person: when the answer is no longer "yes" it stops with the sentence of that answer.
- **T8, taken up: "something waits unsaved" is an answer of its own, `waiting`**, beside `yes`, `no`, `no-permission` and
  `damaged`. The session door and the tab then say: *A “Not this host”, a stop or a withdrawal is not saved yet on
  this side, so nothing was read. Choose “Save now”.* The sentence "the list cannot be read" is for a list that
  cannot be read.
