# PLAN — E6 stories and spec: the `jq` installer

> Status: **plan only, nothing implemented yet, 2026-11-27.** The stories AND the spec of epic E6 of
> [PLAN_the_terminal_panel_shows_the_accounts_and_their_usage.md](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage.md)
> (its *Epics* § *E6*, decision 17, the answer Q3) and of its
> [appendix](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_appendix.md) (§ E the design, § B
> finding F2, § K.38 the epic as approved for epoch 4). Written against `origin/main` at `ec08996e`: extension
> 2.1.0, which holds E2 to E4; the parent is at epoch 5, open, with no change. **ONE document holds the stories
> and the spec: the lead's choice, for time** (the shape of
> [PLAN_the_round_counter_tail_E6.md](PLAN_the_round_counter_tail_E6.md)). **It was written by Fable.**
> `subagent-models.md` asks Opus for a stories document, so §§ 6 to 8 are a deviation, as E3's stories were.
> § 5 is Fable's by that rule: **E6's spec is the cornerstone for area (f), "a change of a machine"**, and the
> consent to it is authorisation (the security row). **The cornerstone mark is made: E6.S1, 3 of 3**
> (the owner's answer to O1, the parent's entry 5.1). This document keeps no change ledger: a change to this epic is an
> entry in the parent. **No code before gate E6-G1 (the Opus review of this document) and E6's plan round.**
>
> Scope: `src_vs_code`, one job of `.github/workflows/ci.yml`, `SECURITY.md`, `research/` and the help. Five
> stories: S1 the script and its reply (the cornerstone); S2 the install "yes" and the record; S3 the install
> in the round; S4 the tab; S5 the documents. Paths are under `src_vs_code/` unless they say otherwise.

## 1. Goal, before any solution

The owner, 2026-11-19 (decision 17; in Russian, translated): *"Install what is needed yourself, and that is
all"*, then *"and only if there are no rights, write to the person: run this command"*. On 2026-11-25 (Q3):
one more one-time "yes" before any install, asked once for all hosts.

Today a place without `jq` cannot be read. Its heading says so and shows the install command with a copy
button (2.1.0). The person must log in to each such place and run the command. On this PC that is the WSL
distro (R10 of E5). When E6 is done:

1. **After ONE more "yes", `jq` is installed where it is missing and needed, when the login user may**: the
   user is root, or has `sudo` that asks no password. The next read then shows the accounts.
2. **Where the user may not, nothing is run.** The tab shows the command, as 2.1.0 does.
3. **Nothing is installed without that "yes".** The permission to read never gives it. A withdrawal stops
   every later install, also against a stale window.
4. **Every install is on record**, and the person reads the record in the tab.

**The owner, 2026-11-26: "we are not building a spaceship".** One package, one fixed script, one question,
one list.

## 2. What exists — extension 2.1.0

| Fact | Where |
|---|---|
| The accounts script names the package manager with one closed word and runs none: `coai_manager`, `COAI-JQ missing <word>` | `src/remoteAccountsScript.ts:176`, `:459`; the words: `:99` |
| Claude Code and Codex each say `unreadable no-jq` when the tool is there and `jq` is not | `src/remoteAccountsScript.ts:288`, `:339` |
| The heading `jq-missing` holds the manager word; the command is a value of `INSTALL_JQ`; the copy looks it up by the place | `src/remoteAccountsTable.ts:481`; `src/remoteAccountsRows.ts:22`; `src/remoteAccountsMessages.ts:44` |
| The permission is `grant: { utc, stamp }` in `refused.json`, in force only above every file `withdrawn-<stamp>.json` and above the copy in the state row | `src/remoteRefusals.ts:62`; `src/remotePermission.ts:26`, `:130`; `src/remoteSessionHost.ts:51`; `src/remoteAccountsStateRow.ts:22` |
| *Allow* and *Withdraw* are `Permit`; it borrows the writer's claim through `Toolkit` | `src/remoteRefusalsPermit.ts:521`, `:19` |
| A key of `refused.json` that a build does not know is kept through a write | `src/remoteRefusals.ts:46`, `:195` |
| The one door `may(who, host)` reads afresh; the round asks it before every call. The one question goes through `notifyAndAsk` | `src/remoteKeeperDoor.ts:128`; `src/remoteAccountsRound.ts:192`; `src/remoteSessionHost.ts:57` |
| The ssh edge: pinned options, 30 s, 64 KiB, the tree killed. The WSL edge: the same, with no `WSLENV` and no `HOME` | `src/remoteSsh.ts:30`, `:108`, `:201`; `src/remoteWsl.ts:29`, `:32`, `:77` |
| CI's extension job has the real `jq`; the accounts script is run for real by `sh` | `.github/workflows/ci.yml:565`; `src/test/remoteAccountsRuns.test.ts` |
| No code installs anything | a search for `install-jq`, `InstallMissingTools` and `terminalAccountsInstalls` in `src` and `package.json`: no line |
| **R10 on this PC:** the WSL distro has no `jq`, for the default user and for root; the manager is `apt-get`; the default user's `sudo` asks for a password. **The VM row is open** | [RESULTS_accounts_e5_long_runs.md](../research/RESULTS_accounts_e5_long_runs.md) |
| A killed `wsl.exe` call leaves no process of that call in the distro (W9e) | [RESULTS_wsl_on_this_pc.md](../research/RESULTS_wsl_on_this_pc.md), (e) |

## 3. What E6 revises of § E

§ E is the design as the gate read it on 2026-11-19, before the one permission, places and WSL. K.38 says
that it is revised when the epic starts.

| § E | E6 builds | Why |
|---|---|---|
| The setting `coai.terminalAccountsInstallMissingTools`, on by default | **Not built.** One install "yes" (§ M2); its withdrawal is the off switch | K.38, Q3 |
| An install on a "yes" of any kind, an automatic one too | Only when the read door says `yes` for that place AND the install "yes" is in force (§ M3) | Epoch 3: one permission |
| The unit is a host | The unit is a place: an ssh host, or one Linux user of one WSL distro (§ M8) | K.38; decision 21 |
| The record is a `globalState` key | A file of the side, under the claim of `refused.json` (§ M3, J6) | Entry 1.2; finding 1 |
| *Its part of Security and Growth*: the record holds 100 rows, and the oldest goes | **Finding 6 supersedes that text:** 1000 rows, none dropped; at the cap no automatic install | § E, point 3 |
| The lock folder `~/.cache/coai-install-jq.lock`; the reply word `failed locked`; one operand | `~/.coai-install-jq.lock`; no word `locked`; two operand words | J2, J3, J7 |
| *Changes CoAI made on a host*, a pick list | A section of the tab (§ M9) | E2: the list is the tab |
| R11, the two settings; R10 (e), a host allowed by itself | Gone; R10 (e) is the consent (§ 8) | The settings and the automatic allow are gone |

**Findings 1, 2 and 6 of the gate hold as § E writes them:** one install at a time (the lock folder on the
target and the store's commit, § M3, § M4); one deadline of 150 s on the target, 180 s here, no retry while a
lock is under 200 s, and only a press retries (§ M4, § M6); a record of up to 1000 rows with none dropped (§ M3).

## 4. Conventions every story follows

- **Worktree** `coai-accounts-e6`, branch `feat/terminal-accounts-e6`, from main. This document's branch is
  `docs/terminal-accounts-e6`.
- **Models.** The spec is § 5 (Fable). Code, tests and fixes: **sonnet**. Review of every story: **opus**.
- **Test ids** are `E6S<story>-T<n>`; container cases are `E6S1-C<n>`; hand rows are `H<n>`.
- **`npm test` never runs the install script with a legal operand.** A developer who is root and has no `jq`
  would get a package installed by a test. The script runs for real only in the containers of `npm run
  test:install`, which CI's Linux runner starts. A scan holds it (E6S1-T5).
- **No test and no agent installs anything on a real machine, runs `sudo` there or starts `wsl.exe`.** In
  `ExtensionMode.Test` the install call is the stub, as the accounts call is.
- **The owner's rows are run once, on one VSIX of E6's build** (§ 8).

## 5. The spec — a change of a machine (the cornerstone, area (f))

Who can try what, and what holds against it: § 9. The sections are M2 to M9.

### M2 — The install "yes"

- **One record of the side**, `install: { utc, stamp }`, in the same locked `refused.json`, beside `grant`. It
  is stored as it is and read by ONE reader, as `grantOf` reads the grant. Build 2.1.0 keeps the key.
- **A withdrawal is a file of its own**, `install-withdrawn-<stamp>.json`, made by an exclusive create. **The
  "yes" is in force only when its stamp is above the stamp of every such file and of its copy**: the ONE
  comparison `inForce`. Every rule of the permission holds for it, with this key and this prefix: the stamp of
  an *Allow* and of a withdrawal, a file that cannot be read counts as a withdrawal, what an *Allow* removes,
  the answer `busy`, *Save now*, and the copy `installWithdrawnStamp` in the state row, which only narrows and
  reaches every side. **Built by widening, not by a copy** (`reuse-first.md`): `Permit`, `inForce`, `floorOf`,
  the disk's withdrawal half and the door's unsaved part take the key and the prefix.
- **Given only by a person's press.** *Allow installing `jq`…* in the tab raises ONE modal through
  `notifyAndAsk`, code `install-jq-permission`, buttons *Allow installing* and *Not now*. No timer, round,
  notice or chat door raises it. It is offered only while the permission to read is in force.
- **The detail says all of what the "yes" covers:**
  - `jq` is installed where it is missing and Claude Code or Codex is there: on every host of an open `Creds:`
    terminal, **the ones open now and the ones opened later**, and for every Linux user of WSL that is read;
    with no further question; once for each of them;
  - with that machine's own package manager (apt-get, dnf, yum or apk), from the repositories that machine
    already uses. **The package's own scripts run as root.** The package lists can be refreshed;
  - as root: where the login user is root, or through `sudo` where it asks no password. Where it asks, CoAI
    runs nothing and shows the command;
  - nothing else is installed, upgraded or removed. Every install is listed in the tab. To stop: *Withdraw*.
- **Withdrawn by a person's press**, in the tab, at once. No install call starts from then on. An install that
  runs is not killed (§ M6).
- **It is never given by the permission to read, and it needs it** (§ M3). Whether *Withdraw the permission*
  of reading also withdraws the install "yes" is the owner's (O4); the door rule holds with both answers.
- **The upgrade starts with no install "yes".** Nothing stored gives one. A key
  `coai.terminalAccountsInstallMissingTools` in a settings file is not read: no release ever read it.
- **The sides**: as the permission. The "yes" is of one side; a withdrawal reaches every side that reads its copy.

### M3 — The install door, and the record

- **The record** is `installs.json` in the folder of the side, beside `refused.json`:
  `{ v: 1, gen, by, rows }`. A row is `{ place, state, manager?, as?, code?, age?, startedUtc, utc }`. The
  place is its key (`ssh:<host>`, `wsl:<distro>/<user>`). **At most 1000 rows and 256 KiB; no row is ever
  removed** (finding 6). It is written only under the claim that `refused.json` is written under, by a
  rename, and read back.
- **The states of a row:** `started`, `installed`, `present`, `no-rights`, `no-manager`, `busy`, `failed`,
  `unreadable` (the reply could not be read), `not-reached` (the call did not reach the place).
- **`begin(place, how)` is ONE operation of the keeper, in its queue, under the claim.** From a fresh read:
  1. the read door says `yes` for the place's scope host (`may('round', …)`);
  2. the install "yes" is in force;
  3. the record can be read;
  4. for `how = timer`: the record has NO row for the place, and fewer than 1000 rows. For `how = press`: the
     place may have a row, but not a `started` row younger than 200 s; a new row needs room;
  5. the row is written as `started`, and read back.

  Only the answer `begun` allows the call. Every other answer names its reason, and no call is made. So the
  row is `started` in the file BEFORE the child is spawned, and two windows give one call (finding 1).
- **`end(place, outcome)`** rewrites the row under the claim, whatever the doors say by then: the record is
  the truth about a machine. If the write fails, the row stays `started`, and the log line holds the outcome.
- **Whatever the outcome, the timer never calls a place that has a row.** Only a press does. A row left at
  `started` by a reload or a crash is not swept: the tab says *an install was started on <day>; how it ended
  is not known*.
- **At the cap** no place gets a new row: no automatic install, and the tab says that the record is full.
- **A record that cannot be read:** no `begin` of either kind, and the tab says so. A person's *Allow
  installing* after a *Withdraw* puts the file aside (one aside file) and starts an empty record (J10).

### M4 — The script

Its shape. Each rule is a row of the tests.

1. **POSIX `sh`. One function, read to its end and then called by ONE brace group**: the call with its
   redirections first, then the end line (the shape of `remoteAccountsScript.ts:468`). A cut text starts nothing.
2. **One operand, of two fixed words:** `install-jq`, and `install-jq-over-stale` for a press on an old lock
   (J7). Anything else: `COAI-BAD operands`, and nothing is run.
3. **Its `PATH` is `/usr/sbin:/usr/bin:/sbin:/bin`**, set and never added to. No `/usr/local`, no home: this
   is the `PATH` that a package's own scripts run with as root (J4).
4. **By a fixed absolute path, never by the `PATH`:** the manager — the first that is executable of
   `/usr/bin/apt-get`, `/usr/bin/dnf`, `/usr/bin/yum`, `/sbin/apk`, the order of `coai_manager` — and
   `/usr/bin/sudo`, `/usr/bin/env`, `/usr/bin/timeout`.
5. **The order.** `jq` is found: `present`. No manager: `no-manager`. The rights: `id -u` is 0, so `root`;
   else `/usr/bin/sudo -n true` succeeds, so `sudo`; else `no-rights <manager>` and **nothing is run**. `-n`
   and a stdin of `/dev/null`: a password is never asked for.
6. **The lock on the target (finding 1).** `mkdir "$HOME/.coai-install-jq.lock"`, then the start time in a
   file `started` inside. The folder is there: `busy <age>`, nothing is run. With `install-jq-over-stale` a
   lock of 200 s or more, or of an age that cannot be read, is removed first: **the script decides that, not
   this side**. The holder removes the lock before its outcome line, and by a `trap`. The folder and that
   file are the only things a script of ours writes in a home.
7. **One deadline for the whole sequence (finding 2):** 150 s from the start. Every manager command gets what
   is left of it, `timeout -k 10 <left>`: a TERM at the deadline and a KILL 10 s later. **`timeout` runs
   INSIDE `sudo`**: a `timeout` of the user cannot signal a process of root. Input and output are `/dev/null`:
   **the manager's output is dropped on the target.** Two variables are set through `/usr/bin/env` (J5).
8. **One refresh.** When the first try fails before the deadline with `apt-get` or `apk`, the lists are
   refreshed once and the command is tried once more.
9. **`jq` is looked for again.** `installed` only when it is found now. Else `failed timeout` (the code is
   124 or 137) or `failed exit`, with the manager's exit code.
10. **Never:** an installer from `curl`, a downloaded binary, a build; a new repository, key or source; an
    option that turns a signature check off; a package but `jq`; an upgrade or a removal; a retry by a timer.

**The table is closed.** The manager word is the only word of a reply that chooses anything, and every string
is a constant of the extension.

| Manager | What the script runs, after `[sudo -n] env … timeout -k 10 <left>` | Refresh | Shown when the user has no rights (`INSTALL_JQ`, as today) |
|---|---|---|---|
| `apt-get` | `/usr/bin/apt-get -o DPkg::Lock::Timeout=60 install -y --no-install-recommends jq` | `/usr/bin/apt-get update` | `sudo apt-get install -y jq` |
| `dnf` | `/usr/bin/dnf install -y jq` | — | `sudo dnf install -y jq` |
| `yum` | `/usr/bin/yum install -y jq` | — | `sudo yum install -y jq` |
| `apk` | `/sbin/apk add jq` | `/sbin/apk update` | `apk add jq` |
| none | nothing | — | the sentence `NO_MANAGER_SAID` |

**The text is written in S1, from these rules; it is not in this document.** S1 makes it the constant
`INSTALL_SCRIPT`. **Its sha256 is fixed by the test E6S1-T1 when the text is written**: that test holds the
constant and a pin line of this section to one another, and a later change of the text changes the pin in the
same commit. A first draft of about 100 lines parses in `dash -n` and in `bash --posix -n` (2026-11-27); only
its `COAI-BAD operands` path was run. **No manager, no `sudo` and no lock path has run anywhere.** The lead
has the draft for the brief of S1.

Pin: sha256 — not fixed yet (E6S1-T1).

### M5 — The reply

The begin line `COAI-INSTALL 1`, ONE line, the end line `COAI-INSTALL-END`; at most 4 KiB. Its own protocol
word: the accounts parser never reads it. The line is one of:

| Line | Values |
|---|---|
| `COAI-JQ present` | — |
| `COAI-JQ installed <m> <as>` | `<m>`: `apt-get`, `dnf`, `yum`, `apk`. `<as>`: `root`, `sudo` |
| `COAI-JQ no-manager` | — |
| `COAI-JQ no-rights <m>` | — |
| `COAI-JQ busy <age>` | 1 to 8 digits, or `-` |
| `COAI-JQ failed <why> <m> <as> <code>` | `<why>`: `timeout`, `exit`. `<code>`: 0 to 255 |
| `COAI-BAD <word>` | `operands`, `home`, `folder`, `tool`, `lock` |

Anything else is `unreadable`: a second line, an extra word, another manager, a longer number, a missing end
line. **No text of a reply is part of a row, a sentence, a command or a log line**: the parser gives closed
words and two numbers. One pure module, with `framed` of `remoteFraming.ts`.

### M6 — The two edges, and their limits

- **ssh:** `ssh <SSH_OPTIONS> -- <host> sh -s -- '<word>'`, by the one argv maker of `remoteSsh.ts` (`:79`).
  `SSH_OPTIONS` does not change.
- **WSL:** `wsl.exe -d <distro> -u <user> --cd / --exec /bin/sh -s -- <word>`, by a builder beside
  `wslAccountsArgv`, through the one spawn function: the door, the running list read again right before the
  call, no `WSLENV`, no `HOME` (the E3.S1 spec, § W3 and § W5).
- **The limits of finding 2 are constants, and a test holds their order:** the script's deadline 150 s and
  its KILL 10 s later; **our ceiling 180 s**, 4 KiB, the tree killed; a lock is old from 200 s. So 160 < 180 <
  200: the target's deadline ends the sequence, and this side normally reads the reply.
- ***Stop the round*, a "no" and a withdrawal do not kill an install that runs.** Its outcome is recorded. No
  further install call starts, and no read follows a door that no longer says `yes`.
- **When our child ends early** (the window closes, the connection drops): over ssh the sequence goes on to
  its own deadline, the lock stays for that time, and the row stays `started`. In WSL: § M8.
- **No code retries an install call.** A press does, and the target refuses it while a lock is young.
- **The log:** one line for each call: the place, the manager, the outcome word, the code. The manager's
  output never leaves the target: it can hold the address of a private repository.

### M7 — The install in the round

After a place's first reply is read, in that place's turn:

1. **Needed?** The reply says `COAI-JQ missing <word>`, the word is not `none`, and Claude Code or Codex says
   `unreadable no-jq`. Else no install: a place with neither tool is logged in to and not changed.
2. `begin(place, timer)`. Not `begun`: the round goes on, and the heading says why (§ M9).
3. The call (§ M6), awaited: one call at a time, as every call of the round.
4. `end(place, outcome)`, the log line, and for `installed` the notice.
5. `installed` or `present`: the round's own checks and the door again; then the place is read once more,
   and the usage rule goes on from that reply. Else the place keeps its reading.

**The timer never asks a person.** A place adds up to 180 s to ONE round, once: 20 such places are an hour
at the worst. How long a real install takes is not measured (R10). **A press** (*Try the install again*) is
`begin(place, press)` and steps 3 to 5 for that one place; while a round runs it is refused with a sentence.

### M8 — A WSL place

- **The install script runs as the user of its place, as a read does.** The `-u` word is the place's user. A
  test fails on `-u root` with an install word for a place that is not `(distro, root)`.
- **Root's place** (decision 24): root is root. When root's place needs `jq` — root has Claude Code or Codex
  there — it is installed as root. `jq` is then there for every user of that distro: their next read shows
  the accounts, and they get no row.
- **A user's place:** `sudo -n true` decides. **On this PC it asks for a password (R10 of E5), so the reply is
  `no-rights apt-get`: nothing is run. The heading shows `sudo apt-get install -y jq` with the copy button —
  what 2.1.0 shows — and now says why.** The owner runs the command once in the distro, and the next round
  reads. **So on this PC's WSL, as R10 records it, E6 installs only if root itself has Claude Code or Codex**;
  R10 does not say if it has. CoAI never installs for a user's place with `-u root`: "nothing of ours as root
  but the users script and root's own place" (decision 24) stands. To change that is the owner's (O3).
- **A stopped distro is never started for an install** (Q2): the running list is read again before the call.
- **A kill is different in WSL (W9e).** No process of a killed `wsl.exe` call is left. So an install in a
  distro does NOT go on when our child ends: a window that closes in those seconds cuts the package manager.
  The lock can stay, and the package database can need a repair. W9e used `sleep`, not a package manager: H1
  (§ 8). Our ceiling is above the script's deadline, so this side cuts no sequence that keeps its time.
- **The login session** (decision 25): nothing new. The read of that place opened it.
- In a window that does not run on Windows there is no WSL install, as there is no WSL read.

### M9 — The tab

- **A foot line under the line of the permission.** No "yes": *CoAI installs nothing on a host.* with *Allow
  installing `jq`…* (only while reading is allowed). Given: *Installing `jq` is allowed since <time>.* with
  *Withdraw*. A change that waits: its sentence and *Save now*, as for the permission.
- **A `jq`-missing heading** keeps its sentence, its command and *Copy install command*. It gains the sentence
  of its row, and with the "yes" in force and a row: *Try the install again*; for `busy` of 200 s or more, or
  of no known age: *Remove the old lock and try again*.

  **Each state has ONE fixed sentence, and S4 writes the list.** Three are fixed here. `no-rights`: *This user
  is not root and has no `sudo` without a password, so CoAI ran nothing.* An old `started`: *An install was
  started on <day>; how it ended is not known.* `failed timeout`: *The install did not end in 150 s and was
  stopped. Check the package manager on that host.* A `not-reached` row holds the runner's own sentence. With
  no row, a record that is full or cannot be read says so.
- **A section *Changes CoAI made on a host***, closed until it is opened: EVERY row of the record — the place,
  what happened, the manager, as root or through `sudo`, the day — also for a place with no open terminal.
- **Four names join the closed list** (`remoteAccountsMessages.ts:22`): `allow-install` raises the modal and
  changes nothing by itself; `withdraw-install` narrows, at once; `install-again <place>` and
  `install-over-stale <place>` name a place of the host's model whose heading is `jq-missing`. The host chooses
  the operand word. No message carries a command, and what the host does not know is dropped.
- **One notice for each install**, code `jq-installed`: *CoAI installed `jq` on <place> with <manager>, as
  root* or *through sudo*. Its one button opens the tab. `notification-sites.json` is generated again.
- Every value is the text of a node, as in the tab today.

## 6. Stories

Order: **S1 and S2 → S3 → S4 → S5.** S1 and S2 share no file and can be coded side by side.

### E6.S1 — The install script and its reply (CORNERSTONE — 3 of 3, the parent's entry 5.1)

`model: fable` (§ M4, § M5) · coder: sonnet · review: opus · area (f).

- **Scope and files.** No call from the extension yet. NEW `src/remoteInstallScript.ts` (the text, its two
  words, its limits) and `src/remoteInstallReply.ts`, both pure; their tests; NEW
  `scripts/run-install-containers.mjs`; `package.json` (the script `test:install`);
  `.github/workflows/ci.yml` (the job `install-containers` on the Linux runner, with the network).

| # | What must be shown (RED first) |
|---|---|
| E6S1-T1 | The sha256 of the constant is the pin line of § M4. The text is ASCII with LF, and parses in `sh -n` |
| E6S1-T2 | No operand, two, another word, a word of the accounts script: `COAI-BAD operands`, by `sh -s`. This path ends before any program is started |
| E6S1-T3 | The cut: the text cut at every byte of its last line, run with a word that is refused, prints no line |
| E6S1-T4 | Every manager command of the text is exactly a row of § M4's table, and the four paths, `sudo`, `env` and `timeout` are absolute. The test reads the table from this document |
| E6S1-T5 | **A scan:** no file of `src/test` or `scripts` but the container run gives the script a legal operand; and the scan finds that one site |
| E6S1-T6 | The parser: every line of § M5 gives its value; each illegal form is `unreadable`; **a test fails if any text of a reply is in the result** |
| E6S1-T7 | The order of the limits: 150 + 10 < 180 < 200; 4 KiB |

**The container cases** — Debian or Ubuntu (D), Fedora (F), Alpine (A), none with `jq`, each image pinned by
digest. A shim at the manager's path logs each start and then runs the real manager: it is how "started" and
"not started" are seen. What is seen here is what § M4 only asks for.

| # | Case | Pass |
|---|---|---|
| E6S1-C1 | As root — D, F, A | `installed <m> root`; `jq` runs; the package set differs by `jq` and what the manager added for it. D ships empty lists: one refresh was seen |
| E6S1-C2 | As root, D with fresh lists | `installed`, and no refresh was started |
| E6S1-C3 | As a user with no `sudo` — D, F, A | `no-rights <m>`; the manager was not started; the package set and the home are as before |
| E6S1-C4 | As a user with `sudo` without a password — D, F | `installed <m> sudo` |
| E6S1-C5 | `jq` there — D, F, A | `present`; the manager was not started; no lock folder was made |
| E6S1-C6 | An image with none of the four managers. Then a `HOME` that is relative, and one that holds `:` | `no-manager`. Then `COAI-BAD home`, and nothing was started |
| E6S1-C7 | **The package lock held** — D. First a plain install is shown to fail on it | `failed exit <code>` inside the deadline; nothing half-installed (`dpkg --audit`); our lock folder is gone |
| E6S1-C8 | **Our lock held (finding 1).** Two calls at once; a lock 100 s old; one 250 s old; one with no time | Two calls: exactly one starts the manager, the other says `busy`. 100 s: `busy` for both words. 250 s and no time: `busy` for `install-jq`; the other word removes it and installs |
| E6S1-C9 | The deadline: a manager that ignores TERM and never ends | `failed timeout` in at most 165 s; no process of the sequence is left; the lock is gone |
| E6S1-C10 | **The ssh client killed in the middle (finding 2).** A real `sshd` in D, the real `ssh` with `SSH_OPTIONS`, a manager that waits 20 s; `ssh` is killed after 5 s | The sequence ends by itself and `jq` is there; a second call in that time says `busy`; then the lock is gone. With the manager of C9: after 165 s no process of the sequence is left |
| E6S1-C11 | `yum` | Run if an image with `yum` is still published; else the summary says that `yum` was not run |

- **Done when.** T1 to T7 green; T3, T5 and T6 seen RED with their break-its; C1 to C10 green on CI's Linux
  runner, and the run's summary names each image with its digest; the pin is in § M4.

### E6.S2 — The install "yes" and the record (SECURITY — Fable's row, outside the quota)

`model: fable` (§ M2, § M3) · coder: sonnet · review: opus · area (c).

- **Scope and files.** No call and no tab yet. `src/remotePermission.ts`, `src/remoteRefusals.ts`,
  `src/remoteRefusalsPermit.ts`, `src/remoteRefusalsDisk.ts`, `src/remoteRefusalsStore.ts`,
  `src/remoteKeeper.ts`, `src/remoteKeeperDoor.ts`, `src/remoteAccountsStateRow.ts`,
  `src/remoteSessionHost.ts`, `src/remoteHostsSaid.ts`; NEW pure `src/remoteInstallRecord.ts`; their tests.

| # | What must be shown (RED first) |
|---|---|
| E6S2-T1 | **The read permission gives no install "yes".** A store of 2.1.0 with a grant, and a settings file with the old key: `begin` says `no-consent` |
| E6S2-T2 | Every test of the permission's stamp rule runs for both keys, by one list of cases |
| E6S2-T3 | **R18 as a test.** The install "yes" is withdrawn in A; A is disposed; a stale B writes `globalState`; a former lock holder renames its stale `refused.json`, which holds the "yes", over the new one; both are built again: `begin` says no, for a tick and for a press |
| E6S2-T4 | The two are apart: an install withdrawal leaves reading in force; with reading withdrawn `begin` says no while the install record stands. The copy only narrows |
| E6S2-T5 | `begin` writes `started` BEFORE it says `begun`: a test fails if the call seam is entered while the file has no row. Two windows on one folder, one with a stale view: one `begun` |
| E6S2-T6 | With a row of each state, `begin(timer)` is refused; `begin(press)` is allowed, but not on a `started` row under 200 s |
| E6S2-T7 | The cap: 1000 rows, then the next place gets `full` and no row is gone; 1000 rows are under 256 KiB |
| E6S2-T8 | `end` writes whatever the doors say; an `end` that fails leaves `started` |
| E6S2-T9 | A record that cannot be read: no `begin`. *Allow installing* after a *Withdraw* puts it aside and starts a new one |
| E6S2-T10 | The modal is raised only by a press, and only while reading is in force; a test fails if the ask seam is called from a tick; *Not now* and Escape write nothing |
| E6S2-T11 | The merge of 2.1.0 keeps the key `install` through its write |

- **Done when.** T1 to T11 green; T1, T3 and T5 seen RED with their break-its.

### E6.S3 — The install in the round

`model: fable` (§ M6 to § M8) · coder: sonnet · review: opus. Depends on S1 and S2. Serves R10, R18, R7.

- **Files.** `src/remoteSsh.ts` and `src/remoteWsl.ts` (the argv, the call, `INSTALL_LIMITS`); NEW pure
  `src/remoteInstallStep.ts` (needed or not, the five steps); `src/remoteAccountsRound.ts`,
  `src/remoteWslRound.ts`, `src/remoteAccountsHost.ts`; their tests; `terminalAccountsScenario.ts`.

| # | What must be shown (RED first) |
|---|---|
| E6S3-T1 | Needed: `jq` missing and a tool `unreadable no-jq` → yes; both tools absent → no; the word `none` → no; `jq` found → no |
| E6S3-T2 | The order: read, `begin`, call, `end`, read again; one call at a time; a second read only after `installed` or `present`, and only after the door said `yes` again |
| E6S3-T3 | **Zero install calls**, by ticks and by presses, with 20 places that lack `jq`: with no install "yes"; for a refused host; while the round is stopped; with `wsl` refused; in a test run with no test door |
| E6S3-T4 | The timer never calls twice: with a row of each state, three rounds make 0 calls; a press makes exactly 1 |
| E6S3-T5 | The argv, word for word, ssh and WSL. A word that is not one of the two throws. WSL: the `-u` word is the place's user; **`-u root` with an install word only for `(distro, root)`**; no `HOME` and no `WSLENV`; a distro that left the running list gets no call |
| E6S3-T6 | 180 s and 4 KiB. A stop or a withdrawal during the call does not kill it; its outcome is in the record; no read follows |
| E6S3-T7 | A call that did not reach is `not-reached` with the runner's sentence; a reply that cannot be read is `unreadable`; the log line holds closed words and one number |
| E6S3-T8 | A press while a round runs is refused with its sentence |
| E6S3-H1 | Host scenario, the call replaced through the test door, the "yes" given through the keeper: one terminal, a place without `jq` → one install call, then a second read; the model holds the accounts, the record one row |

- **Done when.** T1 to T8 green, T3 and T5 seen RED; H1 green in CI's real-editor job.

### E6.S4 — The tab

`model: fable` (§ M9) · coder: sonnet · review: opus. Depends on S2 and S3. Serves R10.

- **Files.** `src/remoteAccountsTable.ts`, `src/remoteAccountsGroups.ts`, `src/remoteAccountsPage.ts`,
  `src/remoteAccountsMessages.ts`, `src/remoteAccountsRun.ts`, `src/remoteAccountsPanel.ts`,
  `notification-sites.json`; their tests.

| # | What must be shown (RED first) |
|---|---|
| E6S4-T1 | The model: the foot line in its three states; each sentence of § M9; the section holds every row; **a test fails if a text of an install reply is part of a command or a sentence** |
| E6S4-T2 | The messages: `allow-install` only raises the modal; `withdraw-install` acts at once; `install-again` for a place the model does not hold, or whose heading is not `jq-missing`, is dropped |
| E6S4-T3 | The page, by RUNNING it: each button posts its message and no other; a place name with markup in it is the text of a node, also in the section; a place's update keeps the section open |
| E6S4-T4 | The notice: one for `installed`, none for another outcome, and none from a tick that installed nothing |

- **Done when.** T1 to T4 green, T1 and T3 seen RED; the log scan covers the new modules.

### E6.S5 — The documents

`model: fable` (this section) · coder: sonnet · review: opus. Last.

- **Files.** The help in five languages; `SECURITY.md`: the three texts that say that the extension only
  reads are changed openly (finding F2 lists them), and it gains the install "yes", the closed table, the
  record, the lock folder, WSL and the residuals of § 9; `research/module_extension.md`,
  `research/module_tests.md` (the container run in the flow catalogue), `research/architecture.md`; the
  release notes; the `POST_DEPLOY.md` question is asked.

- **Tests (RED first).** E6S5-T1: the help census is green, and the install paragraph is in five languages.
  E6S5-T2, a scan: no help text and no line of `SECURITY.md` says that CoAI installs nothing, only reads or
  leaves nothing on a host, but in a sentence that names the install "yes" (break-it: one old sentence put
  back). E6S5-T3: `SECURITY.md` names the install "yes" and its withdrawal, `sudo -n`, the four managers, the
  record, the lock folder and what is not prevented (§ 9).
- **Done when.** T1 to T3 green; the documents say what the head does; `plan-lifecycle.mjs` is clean.

## 7. Build order, and its gates

1. **The owner's answers** to § 11. O1 and O3 can change this document.
2. **Gate E6-G1** — Opus reviews this document against the parent, § E and the code. Then **E6's plan round**
   (`review_plan`). Both before any code.
3. **S1 and S2**, then **S3**, **S4**, **S5**. One pull request or two; the cap of three open ones holds.
4. The code gate (`review_code`, fed the scope) and the own Opus review for each pull request.
5. **The container run is green** on the head that merges, with its job present.
6. **The owner's rows on ONE VSIX of E6's build** (§ 8). **R18, R12 and R7 are gates.** Then the merge.
7. The extension release: the tag pushed alone and its run observed; `POST_DEPLOY.md` run.

## 8. The real-host checks

Run on one VSIX of E6's build. **Owner** = the owner's hands. **Go** = an agent can run it once the owner
gives the go for its exact commands and names the machine. Open from E5: the VM row of R10, the VM half of
R7 and all of R12 have no result on 2.1.0 (O2, O5).

| # | What is run | Pass | Who |
|---|---|---|---|
| R10 (a) | A VM without `jq`, with Claude Code or Codex, reached as root or with `sudo` without a password. Reading is allowed and the install "yes" is NOT given, for 20 minutes. Then *Allow installing* | Before: 0 installs (the VM's package log), and the heading shows the command. After: `jq` is installed once within the next round; the notice and the row are there; the next read shows the accounts; the package log shows one install of `jq` and nothing else installed, upgraded or removed; no service restarted; no install call in the next three rounds | Owner (the presses); Go (the logs) |
| R10 (b) | A user with no rights: **on this PC, the default user of the WSL distro** | Nothing is installed; the row says `no-rights`; the heading shows the exact command and the copy gives it; after the owner runs it, the next round reads the accounts | Owner |
| R10 (c) | A reload with a row in each of `installed`, `no-rights` and `started` | No new install call | Owner |
| R10 (d) | A host with no `jq`, no Claude Code and no Codex | It is logged in to, and nothing is installed | Go |
| R10 (e) | **The consent, in place of the setting.** A second place without `jq` is opened later. Then *Withdraw* of the install "yes". Then *Withdraw the permission* | The question was asked once; the later place is installed with no question. After the first withdrawal: 0 installs in 20 minutes, also after a reload, and the reads go on. After the second: 0 installs and 0 reads | Owner |
| R10 (f) | WSL on this PC. First it is recorded if root has Claude Code or Codex there | No install runs as root for the default user's place (the distro's package log). Root's own place is installed on only if root has a tool | Go |
| **R18** | **Gate (K.38).** The install "yes" is withdrawn in window A; A is closed at once; a stale B writes `globalState` and a stale lock holder writes its file; both are reloaded. Then the same with the permission to read withdrawn | 0 installs from the press on; with reading withdrawn, 0 installs and 0 reads. The folder is read back and holds the withdrawal | Owner |
| **R12** (a)–(d) | **Gate**, on E6's build, as the parent's E5 writes it | As the parent's row | Owner (two real windows) |
| **R7** | **Gate.** Two hours and 12 rounds with a live Claude Code, Codex or agy session, **on a place where an install ran** | 0 live sessions that ask to sign in again, lose their settings or MCP servers, or stop or restart | Go; on a VM the owner answers the creds dialog |
| H1 | WSL, a throw-away distro, as W9 used one: `wsl.exe` is killed in the middle of an install as root | A record: what is left of the manager, of the lock and of the package database | Go |

## 9. Security

**The edge it adds.** The sixth edge of the parent: a call that changes a machine, **as root**, started by a
timer — after two "yes", each by a person's press. It is the only script of ours that writes on a place.

**Finding F2 beside it** (the appendix, § B), part by part:

- *"At once, with no question, on a host nobody was asked about."* Gone: nothing is installed without the
  install "yes". **What stays, by the owner's word (Q3): a host that is opened LATER is covered**, and the
  question says so. So after both "yes", a program that can name a terminal `Creds: <host> / x` can have `jq`
  installed on a host the person reaches as root or with `sudo` without a password, where Claude Code or
  Codex is. What limits it: `known_hosts`, no password ever, one fixed package, once for a place.
- *The time limit that kills the tree.* One deadline on the target; what it can leave is residual I3.
- *"A measure you have not observed is a comment."* Every sentence of § M4 on what `apt-get`, `dnf`, `yum`,
  `apk`, `sudo -n`, `timeout` and `NEEDRESTART_SUSPEND` do is what we ASK. The container cases and R10
  observe it.
- *The three texts that say that the extension only reads.* S5 changes them openly.

**Who gains what** (§ E, read for places):

- *A terminal's name, a file of a workspace, the page:* no "yes". Each "yes" is a press in a modal of the
  extension host; a message of the page names a place and carries no command (§ M9).
- *That Linux user, or root:* nothing. The install runs only where they already are root or have `sudo`
  without a password. A lock folder that they make themselves stops only their own install.
- *Another Linux user of the machine:* nothing by a file they plant: every path is absolute, and the `PATH`
  holds four system folders.
- *Someone on the network, or whoever controls a repository the machine trusts:* what they have when the
  person installs a package by hand — at a time the person did not choose. We add no repository and switch
  no check off.
- *A hostile place:* its reply is one line of closed words. It cannot put a word into a command. It can say
  "no `jq`" on every read; the install is still called once.
- *A stale window, a stale lock holder:* it cannot bring back a withdrawn "yes" (the stamp rule, R18). A row
  it loses costs one more call (I6).

**Residuals.**

- **I1.** The package's own scripts run as root, from whatever repositories the machine is set up with.
- **I2.** A hook of the distribution can restart services. We ask it not to (`NEEDRESTART_SUSPEND=1`): not
  observed, and through `sudo` the machine's own rules can drop the variable.
- **I3.** A manager stopped at the deadline can need a repair by the person. The row says to check it.
- **I4.** In WSL a window that closes during an install cuts it (W9e, H1); the lock can stay.
- **I5.** A refused `sudo -n true` writes a line in that machine's auth log and, where it is set up so, a
  mail to its administrator: once for a place, and once for each press.
- **I6.** A record that is lost — deleted by a program as the person, started anew, or of a second side —
  gives a place one more install call. Beneath it stand `present` and the lock on the target.
- **I7.** The install "yes" is files on one disk: residual 9 of the parent holds for it in the same way.
- **I8.** Two Linux users or two aliases of one machine have two lock folders; the manager's own lock
  decides. **I9.** Refreshed package lists change what a later upgrade by the person sees.

## 10. Growth

- **The record** (`installs.json`): one row, about 150 bytes, for each place where an install was tried. The
  owner's places: tens of rows. The cap: 1000 rows, 256 KiB. **Kept for ever, by decision (finding 6)**, in
  the folder of the side. *Interrupted:* the file is the old one or the new one (a rename); a `started` row is
  shown as it is. One aside file of a damaged record, replaced by the next.
- **The install withdrawal files:** as the permission's — under 200 bytes each, one left by each *Allow*, at
  most 8. **The state row** gains one stamp: under 250 bytes in all.
- **On a place, by our script:** the lock folder with one file of about 11 bytes, removed when the sequence
  ends. After a KILL of its shell it stays, and only a press removes it.
- **On a place, by the install:** the package `jq` and what its manager adds, a few megabytes, not measured,
  once for a machine; and refreshed lists. Nobody retires them. The record lists them.
- **Children:** one install child at a time for a window, killed at 180 s, 4 KiB. **CI:** the containers are
  named `coai-e6-…`, run with `--rm`, and a sweep by that prefix opens the run. **Log lines and notices:** one
  log line for an install call, one notice for an install.

## 11. The owner's questions, and the answers

**Answered by the owner on 2026-11-27, in one answer: every recommended answer below is taken.** O1 and O5
change the parent: its entry 5.1. O2, O3, O4 and O6 are this epic's own and stand as written below.

- **O1 — Is E6 the sixth epic of this plan again, or a plan of its own?** Entry 4.2 moved it "to the next
  release, under a later plan or epic of its own", and counted the quota for five epics: 2 of 2. As the sixth
  epic, Q = ⌈2·6/5⌉ = 3, and the mark of E6.S1 is 3 of 3, as K.38 planned. **Recommended: the sixth epic of
  this plan.** The parent then stays in `todo/` until E6 ships, and its ledger line changes with the mark. The
  lead decides if that recount is entry 5.1; epoch 5 has room for one change.
- **O2 — Start before R10's VM row is known?** E6 serves the owner only where a place lacks `jq` and its
  login user is root or has `sudo` without a password. On this PC's WSL the default user has no such rights
  (§ M8), and the VM row is open. **Recommended: S1 and S2 start now — they touch no machine — and the VM row
  is recorded before the code of S3.** If no place of the owner would get an install, the owner decides again.
- **O3 — In WSL, may CoAI install as root of the distro for a user who has no rights?** `wsl.exe -u root`
  needs no password, so it would work on this PC. It is a third thing of ours as root, against decision 24,
  and the question's text would have to say it. **Recommended: no, not in this release.** The command is
  shown, and a distro costs the owner one command.
- **O4 — Does *Withdraw the permission* of reading also withdraw the install "yes"?** If not, an *Allow
  reading* months later brings the installs back with no new question. **Recommended: yes — one press makes
  both withdrawals.**
- **O5 — R12, and the VM half of R7, have no result on 2.1.0. May their runs on E6's build stand for both?**
  E6's build holds all of 2.1.0's store code. **Recommended: yes; the E5 results then say that 2.1.0 was
  released before them.**
- **O6 — The go for the checks on real machines:** which VM for R10 (a) and (d), and a throw-away distro for
  H1. **Recommended: a VM that the owner can lose, and a distro made for the check, as W9 did.**

No consultant was asked before the owner answered.

## 12. Judgements for gate E6-G1

- **J1.** One document for stories and spec, by Fable (the status block).
- **J2.** The lock is `~/.coai-install-jq.lock`, not under `~/.cache`: one `mkdir`, and no `~/.cache` is made
  in a home that has none. § E left the path to the spec.
- **J3.** The reply has no word `locked`: the manager's exit code does not say it, and its text stays on the
  target. A held package lock gives `failed exit` (C7). § E's test said `failed locked`.
- **J4.** The `PATH` has no `/usr/local`: on some systems a group can write there, and this `PATH` is what a
  package's scripts run with as root.
- **J5.** `DEBIAN_FRONTEND=noninteractive` and `NEEDRESTART_SUSPEND=1`, through `env`, inside `sudo`. Asked,
  not observed.
- **J6.** The record is a file under the claim, not a `globalState` key: a stale window that writes
  `globalState` could put an old record back (entry 1.2), and the "once" guard would go with it.
- **J7.** Two operand words: a press on an old lock has its own word, and the script checks the 200 s itself.
- **J8.** "One `timeout` for the sequence" is built as one deadline, with `timeout -k 10 <left>` for each
  command, inside `sudo`.
- **J9.** ANY row stops the timer, `not-reached` too. **J10.** A record that cannot be read is put aside only
  by *Allow installing* after a *Withdraw*. **J11.** A press is refused while a round runs: no second queue.

**Against the parent.** Scope, the Definition of Done and the merge conditions do not change. J3, J6 and J7
differ from words of § E. K.38 says that § E is revised when the epic starts, and the planner reads them as
that revision, not as entries. **In doubt it is a change: the lead decides before any code.**

**Open:** the script is not written and its draft has not run; H1; the VM row of R10; a `sudo` that runs its
command in a terminal of its own; `yum`.

## 13. Definition of Done — epic E6

- [ ] The owner answered § 11. Gate E6-G1 passed and E6's plan round read this document, before any code.
- [ ] Every test of § 6 is green, and C1 to C10 on CI's Linux runner; each "never" test was seen RED once
      with its message. The sha256 of the script is in § M4 and in its test.
- [ ] **Nothing is installed without the install "yes"**, and the permission to read does not give it. A
      withdrawal stops every later install, also against a stale window and a stale lock holder (R18).
- [ ] **One install at a time for a place, one deadline, and a record of up to 1000 rows from which no row
      goes** (findings 1, 2 and 6). Where the user has no rights nothing is run. No setting was built.
- [ ] No test and no agent installed anything on a real machine. `npm test` never gave the script a legal word.
- [ ] The code gate and the own Opus review ran for each pull request, and every finding is resolved.
- [ ] R10 (a) to (f) and H1 have a recorded result; **R18, R12 and R7 passed on E6's build**, before the merge.
- [ ] `SECURITY.md` and the help say what the head does; `plan-lifecycle.mjs` is clean.
