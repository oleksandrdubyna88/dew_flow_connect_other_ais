# PLAN — appendix to "the terminal panel shows the AI accounts of the open ssh hosts, and their usage"

> Status: **appendix of an open plan; nothing implemented yet, 2026-11-19.** It belongs to
> [PLAN_the_terminal_panel_shows_the_accounts_and_their_usage.md](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage.md)
> and is promoted with it. The plan was 154 KB when the plan gate read it on `653140c3`, and one reviewer could not
> take it whole. What is here was moved out of it **word for word**, so that nothing is lost: only the headings were
> put one level down. § E also holds new text: the gate's findings 1, 2 and 6 on the installer. Where a moved
> section speaks of "this plan", "§ 3" or "below", it means the plan as it stood at `653140c3`; where the plan of
> today says something else, the plan of today holds. Sections R1–R11 and epics E0–E2 named in the moved text are
> those of that revision.

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
