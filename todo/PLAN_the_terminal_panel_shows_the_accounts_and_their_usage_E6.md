# PLAN — E6 stories and spec: the `jq` installer

> Status: **plan only, nothing implemented yet, 2026-11-27.** The stories AND the spec of epic E6 of
> [PLAN_the_terminal_panel_shows_the_accounts_and_their_usage.md](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage.md)
> (its *Epics* § *E6*, decision 17, the answer Q3) and of its
> [appendix](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_appendix.md) (§ E the design, § B
> finding F2, § K.38 the epic as approved for epoch 4). Written against `origin/main` at `ec08996e`: extension
> 2.1.0, which holds E2 to E4. Its line numbers are of that commit; 2.1.1, tagged since, changed lines of
> `remoteAccountsTable.ts` only. **The parent is at epoch 6, open, with no change.** The owner answered § 11 on
> 2026-11-27. Those answers, and what this document changes of § E, are the parent's entries 5.1 to 5.9; the
> owner approved epoch 6 on this text. **ONE document holds the stories
> and the spec: the lead's choice, for time** (the shape of
> [PLAN_the_round_counter_tail_E6.md](PLAN_the_round_counter_tail_E6.md)). **It was written by Fable.**
> `subagent-models.md` asks Opus for a stories document, so §§ 6 to 8 are a deviation, as E3's stories were.
> § 5 is Fable's by that rule: **E6's spec is the cornerstone for area (f), "a change of a machine"**, and the
> consent to it is authorisation (the security row). **The cornerstone mark is made: E6.S1, 3 of 3**
> (the owner's answer to O1, the parent's entry 5.1). This document keeps no change ledger: a change to this epic is an
> entry in the parent. **Gate E6-G1 (Opus, 2026-11-27) read it and gave 21 findings; round 1 of E6's plan round
> (codex) gave 5. This text answers each: § 12.** **No code before E6's plan round passes, and no code of S3
> before the gate of § 7, step 3.**
>
> Scope: `src_vs_code`, one job of `.github/workflows/ci.yml`, `SECURITY.md`, `research/` and the help. Five
> stories: S1 the script and its reply (the cornerstone); S2 the install "yes" and the record; S3 the install
> in the round; S4 the tab; S5 the documents. Paths are under `src_vs_code/` unless they say otherwise.

## 1. Goal, before any solution

The owner, 2026-11-19 (decision 17; in Russian, translated): *"Install what is needed yourself, and that is
all"*, then *"and only if there are no rights, write to the person: run this command"*. On 2026-11-25 (Q3):
one more one-time "yes" before any install, asked once for all hosts.

In 2.1.0 a place without `jq` cannot be read. Its heading says so and shows the install command with a copy
button. The person must log in to each such place and run the command. When E6 is done:

1. **After ONE more "yes", `jq` is installed where it is missing and needed, when the login user may**: the
   user is root, or has `sudo` that asks no password. The next read then shows the accounts.
2. **Where the user may not, nothing is run.** The tab shows the command, as 2.1.0 does, and says why.
3. **Nothing is installed without that "yes".** The permission to read never gives it. A withdrawal stops
   every later install, also against a stale window.
4. **Every install is on record**, and the person reads the record in the tab.

**The owner, 2026-11-26: "we are not building a spaceship".** One package, one fixed script, one question,
one list.

**What this is worth to the owner today — as measured on 2026-11-27** (R10 of E5, § 2). Every place that was
measured has `jq` now, and no login user that was measured has `sudo` without a password. The WSL distro got
`jq` from the owner's own command; the VM had it. **So today E6 would install on no place of the owner.** It
serves a place that is opened later: a new VM or a new distro without `jq`, where the login user is root or
has `sudo` without a password. On every other place it only adds the reason beside the command. Whether root
of the WSL distro has Claude Code or Codex is not recorded. **The gate before S3 (§ 7, step 3) decides with
these facts; if no place of the owner would get an install, the work stops after S1 and S2 and the owner is
asked.**

## 2. What exists — extension 2.1.0

| Fact | Where |
|---|---|
| The accounts script names the package manager with one closed word and runs none: `coai_manager`, `COAI-JQ missing <word>` | `src/remoteAccountsScript.ts:176`, `:459`; the words: `:99` |
| Claude Code and Codex each say `unreadable no-jq` when the tool is there and `jq` is not | `src/remoteAccountsScript.ts:288`, `:339` |
| The heading `jq-missing` holds the manager word; the command is a value of `INSTALL_JQ`; the copy looks it up by the place | `src/remoteAccountsTable.ts:481`; `src/remoteAccountsRows.ts:22`; `src/remoteAccountsMessages.ts:44` |
| The permission is `grant: { utc, stamp }` in `refused.json`, in force only above every file `withdrawn-<stamp>.json` and above the copy in the state row | `src/remoteRefusals.ts:62`; `src/remotePermission.ts:26`, `:130`; `src/remoteSessionHost.ts:51`; `src/remoteAccountsStateRow.ts:22` |
| *Allow* and *Withdraw* are `Permit`; it borrows the writer's claim through `Toolkit`. A withdrawal's file is made by an exclusive create and never waits for the claim; a held claim delays only its read-back | `src/remoteRefusalsPermit.ts:521`, `:19`, `:14` |
| A key of `refused.json` that a build does not know is kept through a write | `src/remoteRefusals.ts:46`, `:195` |
| The one door `may(who, host)` reads afresh, **in the keeper's serial queue**; the round asks it before every call. The one question goes through `notifyAndAsk` | `src/remoteKeeperDoor.ts:128`; `src/remoteKeeper.ts:181`; `src/remoteAccountsRound.ts:192`; `src/remoteSessionHost.ts:57` |
| The ssh edge: pinned options, 30 s, 64 KiB, the tree killed. The WSL edge: the same, with no `WSLENV` and no `HOME`; its one spawn function awaits the door last, then spawns | `src/remoteSsh.ts:30`, `:108`, `:201`; `src/remoteWsl.ts:29`, `:32`, `:77`, `:226` |
| A host scenario puts back what it found: `refused.json`, the markers, the `withdrawn-` files and the locks | `src/remoteSessionHost.ts:549`, `:575` |
| CI's extension job has the real `jq`; the accounts script is run for real by `sh` | `.github/workflows/ci.yml:565`; `src/test/remoteAccountsRuns.test.ts` |
| No code installs anything | a search for `install-jq`, `InstallMissingTools` and `terminalAccountsInstalls` in `src` and `package.json`: no line |
| **R10, recorded 2026-11-27.** The WSL distro had no `jq`, for the default user and for root. The owner ran the shown command at 07:39 UTC (`dpkg`: `jq`, `libjq1`, `libonig5`); both find `/usr/bin/jq` now. The VM's ssh user has `jq`. The manager is `apt-get` on both, and on both the user's `sudo` asks for a password. **Not recorded:** whether root of the distro has Claude Code or Codex | [RESULTS_accounts_e5_long_runs.md](../research/RESULTS_accounts_e5_long_runs.md) |
| A killed `wsl.exe` call leaves no process of that call in the distro (W9e) | [RESULTS_wsl_on_this_pc.md](../research/RESULTS_wsl_on_this_pc.md), (e) |

## 3. What E6 revises of § E

§ E is the design as the gate read it on 2026-11-19, before the one permission, places and WSL. K.38 says
that it is revised when the epic starts **for the consent in place of the setting, and for a WSL place**. What
goes past those two is an entry of the parent's ledger, and the last column names it.

| § E | E6 builds | Why |
|---|---|---|
| The setting `coai.terminalAccountsInstallMissingTools`, on by default | **Not built.** One install "yes" (§ M2); its withdrawal is the off switch | K.38, Q3 |
| An install on a "yes" of any kind, an automatic one too | Only when the read door says `yes` for that place AND the install "yes" is in force (§ M3) | Epoch 3: one permission |
| The unit is a host | The unit is a place: an ssh host, or one Linux user of one WSL distro (§ M8) | K.38; decision 21 |
| The record is a `globalState` key | A file of the side, under the claim of `refused.json` (§ M3, J6) | Entry 1.2; **entry 5.5** |
| One row for a host, rewritten with the outcome | One row for each ATTEMPT, with an id; `end` rewrites only its own row (§ M3) | Round 1, c4; **entry 5.9** |
| *Its part of Security and Growth*: the record holds 100 rows, and the oldest goes | **Finding 6 of § E supersedes that text:** 1000 rows, none dropped; at the cap no install | § E, point 3 |
| The reply word `failed locked` | No word `locked`: a held package lock is `failed exit` or `failed timeout` (J3) | **Entry 5.6** |
| One fixed operand, `install-jq` | Two operand words; the second is a press on an old lock (J7) | **Entry 5.7** |
| "No upgrade of anything else, no removal"; R10 (a): "nothing else installed, upgraded or removed" | `jq` and the packages it needs; a package it needs can be updated; nothing is removed (§ M2, § M4) | Gate findings 3 and 14; **entry 5.8** |
| The lock folder `~/.cache/coai-install-jq.lock` | `~/.coai-install-jq.lock` (J2) | § E: "the spec fixes the path" |
| *Changes CoAI made on a host*, a pick list | A section of the tab (§ M9) | E2: the list is the tab |
| R11, the two settings; R10 (e), a host allowed by itself | Gone; R10 (e) is the consent (§ 8) | The settings and the automatic allow are gone |

**Findings 1, 2 and 6 of § E's gate hold as § E writes them:** one install at a time (the lock folder on the
target and the store's commit, § M3, § M4); one deadline of 150 s on the target, 180 s here, no retry while a
lock is under 200 s, and only a press retries (§ M4, § M6); a record of up to 1000 rows with none dropped (§ M3).

## 4. Conventions every story follows

- **Worktree** `coai-accounts-e6`, branch `feat/terminal-accounts-e6`, from main. This document's branch is
  `docs/terminal-accounts-e6`.
- **Models.** The spec is § 5 (Fable). The scenarios and the brief of a story: Fable for S1 (the cornerstone),
  S2 and S3 (authorisation: the security row); **Opus for S4 and S5**. Code, tests and fixes: **sonnet**.
  Review of every story: **opus**.
- **Test ids** are `E6S<story>-T<n>`; container cases are `E6S1-C<n>`; hand rows are `H<n>`.
- **`npm test` never runs the install script with a legal operand.** A developer who is root, or who has
  `sudo` without a password, and has no `jq` would get a package installed by a test. The script runs for real
  only in the containers of `npm run test:install`, which CI's Linux runner starts. Two things hold it: a scan
  (E6S1-T5), and **a guard at run time: the ONE helper that runs the script refuses a legal operand unless
  `COAI_INSTALL_CONTAINER=1`, which only the container's own entry sets** (gate finding 21).
- **No test reads a file of `todo/`**: a plan moves when it is promoted. The command table and the pin are
  constants of the code and literals of the tests (gate finding 16).
- **No test and no agent installs anything on a real machine, runs `sudo` there or starts `wsl.exe`.** In
  `ExtensionMode.Test` the install call is the stub, as the accounts call is.
- **The owner's rows are run once, on one VSIX of E6's build** (§ 8), on a VM and a distro that can be lost
  (the owner's answer O6).

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
  the disk's withdrawal half and the door's unsaved part take the key and the prefix. The prefix
  `install-withdrawn-` does not start with `withdrawn-`, so the reader of one family never takes a file of
  the other.
- **Given only by a person's press.** *Allow installing `jq`…* in the tab raises ONE modal through
  `notifyAndAsk`, code `install-jq-permission`, buttons *Allow installing* and *Not now*. No timer, round,
  notice or chat door raises it. It is offered only while the permission to read is in force. **The write
  checks that again: under the claim, from a fresh read, the permission to read is in force — or nothing is
  written, and the person is told** (gate finding 19).
- **The detail says all of what the "yes" covers:**
  - `jq` is installed where it is missing and Claude Code or Codex is there: on every host of an open `Creds:`
    terminal, **the ones open now and the ones opened later**, and for every Linux user of WSL that is read;
    with no further question; once for each of them;
  - with that machine's own package manager (apt-get, dnf, yum or apk), from the repositories that machine
    already uses; the manager can accept a signing key that one of those repositories names. **The package's
    own scripts run as root.** The package lists can be refreshed;
  - as root: where the login user is root, or through `sudo` where it asks no password. Where it asks, CoAI
    runs nothing and shows the command;
  - **`jq` and the packages that `jq` needs are installed; a package that it needs can be updated; nothing is
    removed.** Every install is listed in the tab. To stop: *Withdraw*; *Withdraw the permission* of reading
    stops the installs too.
- **Withdrawn by a person's press**, in the tab, at once. No install call starts from then on (§ M3, step 6).
  An install that runs is not killed (§ M6).
- **It is never given by the permission to read, and it needs it** (§ M3).
- ***Withdraw the permission* of reading withdraws the install "yes" too — the owner's answer O4.** That one
  press FIRST makes `install-withdrawn-<stamp>.json` and THEN the withdrawal of reading. It never stops after
  the first write. A write that fails takes the permission's road for a withdrawal that was not saved, each
  file for itself: its window holds it, copies its stamp to the state row at once, and offers *Save now*. So
  an *Allow reading* given later brings no install back: the install "yes" stays below its withdrawal until a
  person presses *Allow installing* again. An *Allow reading* never writes the key `install`.
- **The upgrade starts with no install "yes".** Nothing stored gives one. A key
  `coai.terminalAccountsInstallMissingTools` in a settings file is not read: no release ever read it.
- **The sides**: as the permission. The "yes" is of one side; a withdrawal reaches every side that reads its copy.

### M3 — The install door, and the record

- **The record** is `installs.json` in the folder of the side, beside `refused.json`:
  `{ v: 1, gen, by, rows }`. **A row is ONE attempt** (round 1, c4):
  `{ id, place, how, state, manager?, as?, code?, age?, startedUtc, utc? }`. The `id` is 16 hex digits that
  this side makes at random. `how` is `timer` or `press`. The place is `ssh:<host>` or `wsl:<distro>/<user>`.
  **At most 1000 rows and 512 KiB; no row is ever removed, and none is used for a second attempt**
  (finding 6). It is written only under the claim that `refused.json` is written under, by a rename, and read
  back.
- **The states of a row:** `started`, `installed`, `present`, `no-rights`, `no-manager`, `busy`, `failed`,
  `unreadable` (the reply could not be read), `not-reached` (the call did not reach the place, or was not
  started).
- **`begin(place, how, start)` is ONE operation of the keeper, in its queue, under the claim.** It never
  calls the queued `may()`: that call would wait behind `begin` itself, for ever (`remoteKeeper.ts:181`,
  `remoteKeeperDoor.ts:128`; gate finding 18). It makes its own fresh read and gives it to the same pure
  rule that `may` uses. From that read:
  1. the read door's answer for the place's scope host, as for `may('round', …)`, is `yes`;
  2. the install "yes" is in force;
  3. the record can be read;
  4. for `how = timer`: the record has NO row for the place. For `how = press`: the place may have rows, but
     its newest is not a `started` row younger than 200 s. For both: the record has room for a row;
  5. a new row is written as `started`, with its id, and read back;
  6. **the last look, and the spawn** (gate finding 5; round 1, c1). Everything that can change without the
     claim is read once more: the withdrawal files of both families, and the state row. Both answers are
     still `yes`: `start()` is called. It spawns the child, **as the last act under the claim, with nothing
     awaited between the look and the spawn**. One answer is not `yes`: the row becomes `not-reached`, with
     the fixed sentence *The install was not started: a permission changed first*, and no child is spawned.

  Only the answer `begun` — with the attempt's id and the child — means a call. Every other answer names its
  reason, and no call is made. So the row is `started` in the file BEFORE the child is spawned, and two
  windows give one call (§ E, finding 1).
- **A withdrawal and the spawn cannot pass one another.** A withdrawal's file is made without the claim, but
  its window does not say *withdrawn* while another window holds the claim (`remoteRefusalsPermit.ts:14`). So a
  withdrawal is seen by the last look, and no child is spawned; or its word *withdrawn* comes after the claim
  is given back, when the child runs already: "an install that runs" (§ M6). What is left is residual I11.
- **`end(id, outcome)`** rewrites ONLY the row with that id, and only while its state is `started`; under the
  claim, whatever the doors say by then: the record is the truth about a machine. **An `end` that comes late
  cannot land on a newer attempt.** No such row — a new record was started in between — or a write that
  fails: nothing is rewritten, a `started` row stays, and the log line holds the outcome.
- **Whatever the outcome, the timer never calls a place that has a row.** Only a press does. A row left at
  `started` by a reload or a crash is not swept: the tab says *an install was started on <day>; how it ended
  is not known*.
- **At the cap** no attempt gets a row, by the timer or by a press: no install, and the tab says that the
  record is full and shows the command.
- **A record that cannot be read** — it is not JSON, not this shape, or above a cap: no `begin` of either
  kind, and the tab says so. It is never repaired and never written over. Only a person's *Allow installing*
  after a *Withdraw* starts a new record (J10). FIRST the file is renamed to
  `installs-damaged-<stamp>.json`, with the stamp of that *Allow*: a name that no earlier file has. THEN an
  empty record is made. **A file put aside is never replaced, removed or written by us** (round 1, c5). With
  8 such files in the folder no new record is started, and the tab says to move them away by hand. The tab
  names every one of them, with its day and the folder (§ M9); their rows cannot be read, so they are not
  shown. A new record costs the "once" of the old rows (I6).

### M4 — The script

Its shape. Each rule is a row of the tests.

1. **POSIX `sh`. One function, read to its end and then called by ONE brace group**: the call with its
   redirections first, then the end line (the shape of `remoteAccountsScript.ts:468`). A cut text starts nothing.
2. **One operand, of two fixed words:** `install-jq`, and `install-jq-over-stale` for a press on an old lock
   (J7). Anything else: `COAI-BAD operands`, and nothing is run.
3. **Its `PATH` is `/usr/sbin:/usr/bin:/sbin:/bin`**, set and never added to. No `/usr/local`, no home (J4).
   **`sudo` puts its own `PATH` in place of that one** — on Debian and Ubuntu its `secure_path` starts with
   `/usr/local` (gate finding 2). So the same four folders are set once more AFTER `sudo`, by `env`, in
   front of every manager command (rule 7): that is the `PATH` a package's own scripts run with as root.
4. **By a fixed absolute path, never by the `PATH`:** the manager — the first that is executable of
   `/usr/bin/apt-get`, `/usr/bin/dnf`, `/usr/bin/yum`, `/sbin/apk`, the order of `coai_manager` — and
   `/usr/bin/sudo`, `/usr/bin/env`, `/usr/bin/timeout`.
5. **The order.** `jq` is found: `present`. No manager: `no-manager`. The rights: `id -u` is 0, so `root`;
   else `/usr/bin/timeout 10 /usr/bin/sudo -n true` succeeds, so `sudo`; else `no-rights <manager>` and
   **nothing is run**. `-n` and a stdin of `/dev/null`: a password is never asked for. The 10 s bound a
   `sudo` that waits for a directory server (gate finding 10). `sudo -n true` shows only that `true` may
   run: where the machine's rules allow some commands and not ours, the sequence ends `failed exit`, and
   nothing ran as root.
6. **The lock on the target (§ E, finding 1).**
   - **Taking it:** `mkdir "$HOME/.coai-install-jq.lock"`, then the file `started` inside: the start time
     and the pid of this shell, `<seconds> <pid>`. `mkdir` makes the folder or fails.
   - **The folder is there:** `busy <age>`, and nothing is run. The age is read from `started`; one that
     cannot be read, or is above 99 999 999, is `-` (gate finding 12).
   - **With `install-jq-over-stale`** an old lock is taken away first: **the script decides that, not this
     side.** A lock is old when its time is 200 s or more ago. **A lock with no time that can be read is not
     old at once**: its holder writes the time right after its `mkdir`. The script waits 2 s and reads
     again; only a lock that still has no time is old (round 1, c2).
   - **An old lock is taken away by a rename, never where it stands** (gate finding 9): `mv` to a name of
     this call's own, `….lock.<pid>`. Then the moved folder's `started` is compared with the text that was
     judged old. It is that text: the moved folder is removed, with all that is in it, and the lock is
     taken by the plain `mkdir`, which can lose (`busy`). It is another text — a second call took the old
     lock away first, and this call moved the NEW lock: that lock is put back at once — its name is made
     again by `mkdir`, and it gets its holder's `started` — and the reply is `busy`. If the name is taken
     by then, nothing is put back. So two calls that judge one lock old start the manager once (C8). What
     is left is residual I8.
   - **Giving it back:** the holder removes its lock before its outcome line, and by a `trap` — but only
     while `started` still holds its own text. A holder whose lock was taken away as old removes nothing.
   - The lock folder, its one file and, for an instant, the moved folder are the only things a script of
     ours writes in a home.
7. **One deadline for the whole sequence (§ E, finding 2):** 150 s from the start. Every manager command gets
   what is left of it, `timeout -k 10 <left>`: a TERM at the deadline and a KILL 10 s later. **`timeout` runs
   INSIDE `sudo`**: a `timeout` of the user cannot signal a process of root. Input and output are `/dev/null`:
   **the manager's output is dropped on the target.** The whole call line of a manager command is
   `[/usr/bin/sudo -n] /usr/bin/env PATH=/usr/sbin:/usr/bin:/sbin:/bin DEBIAN_FRONTEND=noninteractive
   NEEDRESTART_SUSPEND=1 /usr/bin/timeout -k 10 <left> <the row of the table>`. The three variables are set
   after `sudo`, so no rule of `sudo` drops them (J5).
8. **One refresh.** When the first try fails before the deadline with `apt-get` or `apk`, the lists are
   refreshed once and the command is tried once more. A try that the deadline ended starts no refresh.
9. **`jq` is looked for again.** `installed` only when it is found now. Else `failed timeout` — the code is
   124, 137 or 143 — or `failed exit`, with the manager's exit code. **143 is for Alpine:** its `timeout` is
   BusyBox's, which — from memory, not verified — ends 143 at a TERM, and an old one has no `-k`. C1 and C9
   run on Alpine and decide; if `-k` is refused there, this rule is revised before any code (gate finding 11).
10. **Never:** an installer from `curl`, a downloaded binary, a build; a new repository or source; a key
    that the machine's repositories do not already name; an option that turns a signature check off; a
    package but `jq` and what the manager adds for it; an option that allows a removal; a retry by a timer.

**The table is closed.** The manager word is the only word of a reply that chooses anything, and every string
is a constant of the extension.

| Manager | What the script runs, after the call line of rule 7 | Refresh | Shown when the user has no rights (`INSTALL_JQ`, as today) |
|---|---|---|---|
| `apt-get` | `/usr/bin/apt-get -o DPkg::Lock::Timeout=60 install -y --no-install-recommends --no-remove jq` | `/usr/bin/apt-get update` | `sudo apt-get install -y jq` |
| `dnf` | `/usr/bin/dnf install -y --setopt=install_weak_deps=False jq` | — | `sudo dnf install -y jq` |
| `yum` | `/usr/bin/yum install -y jq` | — | `sudo yum install -y jq` |
| `apk` | `/sbin/apk add jq` | `/sbin/apk update` | `apk add jq` |
| none | nothing | — | the sentence `NO_MANAGER_SAID` |

**What holds "nothing is removed"** (gate findings 3 and 14). `apt-get` gets `--no-remove`, on the first try
and on the second: an install that would remove a package stops. `dnf` gets no `--allowerasing`, and no weak
dependencies. `yum` and `apk` get no option for it: that they remove nothing for an install is what C1 sees
on its images, not a thing we set. A package that `jq` needs can be updated by any of the four.

**The text is written in S1, from these rules; it is not in this document.** S1 makes it the constant
`INSTALL_SCRIPT`, and the table above the constant `INSTALL_COMMANDS`, both in `remoteInstallScript.ts`.
**The sha256 of the text is a literal of the test E6S1-T1.** A later change of the text changes that literal
and the pin line below in the same commit; no test reads this document. A first draft of about 100 lines
parses in `dash -n` and in `bash --posix -n` (2026-11-27); only its `COAI-BAD operands` path was run. It
does not have rules 3, 5, 6 and 9 as they stand now. **No manager, no `sudo` and no lock path has run
anywhere.** The lead has the draft for the brief of S1.

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
  `wslAccountsArgv`, with no `WSLENV` and no `HOME` (the E3.S1 spec, § W3 and § W5).
- **The order of a call, for both edges: ready → `begin` → spawn** (gate finding 5). *Ready:* the platform,
  the program's file and the argv are made first. In WSL the running list is read again, behind the read
  door; a distro that is not in it gets no `begin`, no row and no call. *Then* `begin`, whose last act is
  the spawn (§ M3, step 6). **The install call has no awaited door of its own**: the last look of `begin`
  is its door, and `start` reaches `capture` with nothing awaited. The accounts call keeps its own door.
- **The limits of § E's finding 2 are constants, and a test holds their order:** the script's deadline 150 s
  and its KILL 10 s later; **our ceiling 180 s**, 4 KiB, the tree killed; a lock is old from 200 s. So 160 <
  180 < 200: the target's deadline ends the sequence, and this side normally reads the reply.
- ***Stop the round*, a "no" and a withdrawal do not kill an install that runs.** Its outcome is recorded. No
  further install call starts, and no read follows a door that no longer says `yes`.
- **When our child ends early** (the window closes, the connection drops): over ssh the sequence goes on to
  its own deadline, the lock stays for that time, and the row stays `started` — but for residual I10. In
  WSL: § M8.
- **No code retries an install call.** A press does, and the target refuses it while a lock is young.
- **The log:** one line for each call: the place, the manager, the outcome word, the code. The manager's
  output never leaves the target: it can hold the address of a private repository.

### M7 — The install in the round

After a place's first reply is read, in that place's turn:

1. **Needed?** The reply says `COAI-JQ missing <word>`, the word is not `none`, and Claude Code or Codex says
   `unreadable no-jq`. Else no install: a place with neither tool is logged in to and not changed.
2. **Ready** (§ M6). A distro that left the running list: no `begin`; the heading says *not running*.
3. `begin(place, timer, start)`. Not `begun`: the round goes on, and the heading says why (§ M9).
4. The child's end is awaited: one call at a time, as every call of the round.
5. `end(id, outcome)`, the log line, and for `installed` the notice.
6. `installed` or `present`: the round's own checks and the door again; then the place is read once more,
   and the usage rule goes on from that reply. Else the place keeps its reading.

**The timer never asks a person.** A place adds up to 180 s to ONE round, once: 20 such places are an hour
at the worst. How long a real install takes is not measured (R10). **A press** (*Try the install again*) is
steps 2 to 6 with `press`, for that one place; while a round runs it is refused with a sentence.

### M8 — A WSL place

- **The install script runs as the user of its place, as a read does.** The `-u` word is the place's user. A
  test fails on `-u root` with an install word for a place that is not `(distro, root)`.
- **Root's place** (decision 24): root is root. When root's place needs `jq` — root has Claude Code or Codex
  there — it is installed as root. `jq` is then there for every user of that distro: their next read shows
  the accounts, and they get no row.
- **A user's place:** `sudo -n true` decides. Where it asks for a password, the reply is `no-rights
  <manager>`: nothing is run, and the heading shows the command with the copy button — what 2.1.0 shows —
  and now says why. **CoAI never installs for a user's place with `-u root`** (the owner's answer O3):
  "nothing of ours as root but the users script and root's own place" (decision 24) stands. A distro costs
  the owner one command. **On this PC that is what happened** (§ 2): the default user's `sudo` asks for a
  password, the owner ran the command once, and the distro has `jq` now.
- **A stopped distro is never started for an install** (Q2): the running list is read again before `begin`.
  One that stops between that read and the spawn is started by the call: the parent's residual 12.
- **A kill is different in WSL (W9e).** No process of a killed `wsl.exe` call is left. So an install in a
  distro does NOT go on when our child ends: a window that closes in those seconds cuts the package manager.
  The lock can stay, and the package database can need a repair. W9e used `sleep`, not a package manager; H1
  (§ 8) would show it with one, and is run only if a root place would get an install. Our ceiling is above
  the script's deadline, so this side cuts no sequence that keeps its time.
- **The login session** (decision 25): nothing new. The read of that place opened it.
- In a window that does not run on Windows there is no WSL install, as there is no WSL read.

### M9 — The tab

- **A foot line under the line of the permission.** No "yes": *CoAI installs nothing on a host.* with *Allow
  installing `jq`…* (only while reading is allowed). Given: *Installing `jq` is allowed since <time>.* with
  *Withdraw*. A change that waits: its sentence and *Save now*, as for the permission. **The line of the
  permission says that *Withdraw the permission* stops the installs too** (O4).
- **A `jq`-missing heading** keeps its sentence, its command and *Copy install command*. It gains the sentence
  of its newest row, and with the "yes" in force and a row: *Try the install again*; for `busy` of 200 s or
  more, or of no known age: *Remove the old lock and try again*.

  **Each state has ONE fixed sentence, and S4 writes the list.** Four are fixed here. `no-rights`: *This user
  is not root and has no `sudo` without a password, so CoAI ran nothing.* An old `started`: *An install was
  started on <day>; how it ended is not known.* `failed timeout`: *The install did not end in 150 s and was
  stopped. Check the package manager on that host.* `not-reached` from the last look: *The install was not
  started: a permission changed first.* Another `not-reached` row holds the runner's own sentence. With no
  row, a record that is full or cannot be read says so.
- **A section *Changes CoAI made on a host***, closed until it is opened: EVERY row of the record, so every
  attempt — the place, what happened, the manager, as root or through `sudo`, by the timer or by a press,
  the day — also for a place with no open terminal. Under them, one line for each file that was put aside:
  *An older record could not be read. It is kept as <name>, in <folder>.*
- **Four names join the closed list** (`remoteAccountsMessages.ts:22`): `allow-install` raises the modal and
  changes nothing by itself; `withdraw-install` narrows, at once; `install-again <place>` and
  `install-over-stale <place>` name a place of the host's model whose heading is `jq-missing`. The host chooses
  the operand word. No message carries a command, and what the host does not know is dropped.
- **One notice for each install**, code `jq-installed`: *CoAI installed `jq` on <place> with <manager>, as
  root* or *through sudo*. Its one button opens the tab. `notification-sites.json` is generated again.
- Every value is the text of a node, as in the tab today.

## 6. Stories

Order: **S1 and S2 → the gate of § 7, step 3 → S3 → S4 → S5.** S1 and S2 share no file and can be coded side
by side.

### E6.S1 — The install script and its reply (CORNERSTONE — 3 of 3, the parent's entry 5.1)

`model: fable` (§ M4, § M5) · coder: sonnet · review: opus · area (f).

- **Scope and files.** No call from the extension yet. NEW `src/remoteInstallScript.ts` (the text, its two
  words, the command table, its limits) and `src/remoteInstallReply.ts`, both pure; their tests; NEW
  `scripts/run-install-containers.mjs`; `package.json` (the script `test:install`);
  `.github/workflows/ci.yml` (the job `install-containers` on the Linux runner, with the network).

| # | What must be shown (RED first) |
|---|---|
| E6S1-T1 | The sha256 of the constant is a literal of this test. The text is ASCII with LF, and parses in `sh -n`. The test reads no file of `todo/` |
| E6S1-T2 | No operand, two, another word, a word of the accounts script: `COAI-BAD operands`, by `sh -s`. This path ends before any program is started |
| E6S1-T3 | The cut: the text cut at every byte of its last line, run with a word that is refused, prints no line |
| E6S1-T4 | Every manager command of the text is exactly a row of the constant `INSTALL_COMMANDS`, which the test holds as a literal of its own; the call line of rule 7 stands in front of each, with `PATH` and the two variables after `sudo`; the four paths, `sudo`, `env` and `timeout` are absolute; the `sudo -n true` probe is under `timeout 10` |
| E6S1-T5 | **A scan:** no file of `src/test` or `scripts` but the container run gives the script a legal operand; and the scan finds that one site. **The guard:** the helper that runs the script throws on a legal operand without `COAI_INSTALL_CONTAINER=1` |
| E6S1-T6 | The parser: every line of § M5 gives its value; each illegal form is `unreadable`, a `busy` age of 9 digits among them; **a test fails if any text of a reply is in the result** |
| E6S1-T7 | The order of the limits: 150 + 10 < 180 < 200; 4 KiB |

**The container cases** — Debian or Ubuntu (D), Fedora (F), Alpine (A), none with `jq`, each image pinned by
digest. A shim at the manager's path logs each start, **with its `PATH` and its environment**, and then
runs the real manager: it is how "started", "not started" and rule 7 are seen. What is seen here is what
§ M4 only asks for.

| # | Case | Pass |
|---|---|---|
| E6S1-C1 | As root — D, F, A | `installed <m> root`; `jq` runs; the package set differs by `jq` and what the manager added or updated for it, and **no package is gone**. D ships empty lists: one refresh was seen |
| E6S1-C3 | As a user with no `sudo` — D, F, A | `no-rights <m>`; the manager was not started; the package set and the home are as before |
| E6S1-C4 | As a user with `sudo` without a password — D with fresh lists, F. The user's `sudo` has a `secure_path` that starts with `/usr/local` | `installed <m> sudo`; on D no refresh was started; **the shim saw `PATH=/usr/sbin:/usr/bin:/sbin:/bin`, `DEBIAN_FRONTEND` and `NEEDRESTART_SUSPEND`** (gate finding 2) |
| E6S1-C5 | `jq` there — D, F, A | `present`; the manager was not started; no lock folder was made |
| E6S1-C6 | An image with none of the four managers. Then a `HOME` that is relative, and one that holds `:` | `no-manager`. Then `COAI-BAD home`, and nothing was started |
| E6S1-C7 | **The package lock held** — D. First a plain install is shown to fail on it | `failed exit <code>` or `failed timeout`, in at most 165 s: the two waits of 60 s and the refresh stand close to the deadline, so either word passes (gate finding 15). Nothing half-installed (`dpkg --audit`); our lock folder is gone |
| E6S1-C8 | **Our lock held (§ E, finding 1).** (a) Two calls at once. (b) A lock 100 s old; one 250 s old. (c) A lock with no `started`: made 1 s before the call, and one that stays so. (d) **Two calls with the second word at once, on one lock 250 s old.** (e) A holder whose lock was taken away as old ends | (a) Exactly one starts the manager, the other says `busy`. (b) 100 s: `busy` for both words. 250 s: `busy` for `install-jq`; the other word takes it away and installs. (c) The young one: `busy -` for both words, and it is not removed; the one that stays: the second word installs after its 2 s. (d) The manager is started once; one reply is `busy`. (e) The newer holder's lock is still there |
| E6S1-C9 | The deadline: a manager that ignores TERM and never ends — **D, F and A** | `failed timeout` in at most 165 s; no process of the sequence is left; the lock is gone; no refresh was started. On A the code that BusyBox gives is recorded (gate finding 11) |
| E6S1-C10 | **The ssh client killed in the middle (§ E, finding 2).** A real `sshd` in D, the real `ssh` with `SSH_OPTIONS`, a manager that waits 20 s; `ssh` is killed after 5 s | The sequence ends by itself and `jq` is there; a second call in that time says `busy`; then the lock is gone. With the manager of C9: after 165 s no process of the sequence is left. **The container has no `logind`: it cannot show residual I10** |
| E6S1-C11 | `yum` — optional, it gates nothing | Run if an image with `yum` and no `dnf` is still published; else the summary says that `yum` was not run, and `SECURITY.md` says that its row was never seen |

- **Done when.** T1 to T7 green; T3, T5 and T6 seen RED with their break-its; C1 and C3 to C10 green on
  CI's Linux runner, and the run's summary names each image with its digest; the pin is in § M4. (There is
  no C2: gate E6-G1 cut it, and C4 holds what it showed.)

### E6.S2 — The install "yes" and the record (SECURITY — Fable's row, outside the quota)

`model: fable` (§ M2, § M3) · coder: sonnet · review: opus · area (c).

- **Scope and files.** No call and no tab yet; `begin` takes its `start` as a seam. `src/remotePermission.ts`,
  `src/remoteRefusals.ts`, `src/remoteRefusalsPermit.ts`, `src/remoteRefusalsDisk.ts`,
  `src/remoteRefusalsStore.ts`, `src/remoteKeeper.ts`, `src/remoteKeeperDoor.ts`,
  `src/remoteAccountsStateRow.ts`, `src/remoteSessionHost.ts`, `src/remoteHostsSaid.ts`; NEW pure
  `src/remoteInstallRecord.ts`; their tests.

| # | What must be shown (RED first) |
|---|---|
| E6S2-T1 | **The read permission gives no install "yes".** A store of 2.1.0 with a grant, and a settings file with the old key: `begin` says `no-consent` |
| E6S2-T2 | Every test of the permission's stamp rule runs for both keys, by one list of cases; a file of one family is never read as the other's |
| E6S2-T3 | **R18 as a test.** The install "yes" is withdrawn in A; A is disposed; a stale B writes `globalState`; a former lock holder renames its stale `refused.json`, which holds the "yes", over the new one; both are built again: `begin` says no, for a tick and for a press |
| E6S2-T4 | The two are apart: an install withdrawal leaves reading in force; with reading withdrawn `begin` says no while the install record stands. The copy only narrows |
| E6S2-T5 | `begin` writes `started` BEFORE `start` is entered: a test fails if the seam is entered while the file has no row. Two windows on one folder, one with a stale view: one `begun` |
| E6S2-T6 | With a row of each state, `begin(timer)` is refused; `begin(press)` adds a NEW row, but not while the place's newest row is `started` under 200 s |
| E6S2-T7 | The cap: 1000 rows, then `full` for a timer and for a press, and no row is gone; 1000 rows of the longest legal place are under 512 KiB |
| E6S2-T8 | `end` writes whatever the doors say; an `end` that fails leaves `started` |
| E6S2-T9 | A record that cannot be read: no `begin`. *Allow installing* after a *Withdraw* renames it to a stamped name and starts a new one; **a second damaged record gets a second name, and the first file is as it was**; with 8 such files no new record is started |
| E6S2-T10 | The modal is raised only by a press, and only while reading is in force; a test fails if the ask seam is called from a tick; *Not now* and Escape write nothing |
| E6S2-T11 | The merge of 2.1.0 keeps the key `install` through its write |
| E6S2-T12 | **O4.** *Withdraw the permission* makes the install withdrawal first, then the one of reading; then *Allow reading*: `begin` says `no-consent` until a new *Allow installing*. The first write fails: the second is still tried, and the first waits as "not saved" |
| E6S2-T13 | The write of *Allow installing* with reading withdrawn in another window after the modal opened: nothing is written (gate finding 19) |
| E6S2-T14 | **An attempt has its own row** (c4). An attempt ends `busy`; a press begins a second; the first `end` comes late with `failed`: the first row is `failed`, the second is still `started`. An `end` for an id that the record does not hold rewrites nothing |
| E6S2-T15 | **The last look** (finding 5, c1). A withdrawal file of either family that appears after the row is written and before the look: `start` is not entered, and the row is `not-reached` with its sentence. A withdrawal made while `begin` holds the claim: its window does not say *withdrawn* before the claim is given back. A test fails if anything is awaited between the look and `start` |
| E6S2-T16 | `begin` runs to its end in the real queue, with `may()` called by another caller at the same time; a test fails if `begin` calls the queued `may()` (gate finding 18) |
| E6S2-T17 | The scenario's put-back covers `installs.json`, the `install-withdrawn-` files and the files put aside: a scenario that made each leaves the folder as it found it (gate finding 17) |

- **Done when.** T1 to T17 green; T1, T3, T5 and T15 seen RED with their break-its.

### E6.S3 — The install in the round (SECURITY — Fable's row, outside the quota)

`model: fable` (§ M6 to § M8) · coder: sonnet · review: opus · area (e), who runs as whom in a distro, and
area (c), the door in front of an install call: both are authorisation, so the mark stays Fable's (gate
finding 8). Depends on S1, S2 and the gate of § 7, step 3. Serves R10, R18, R7.

- **Files.** `src/remoteSsh.ts` and `src/remoteWsl.ts` (the argv, the call, `INSTALL_LIMITS`); NEW pure
  `src/remoteInstallStep.ts` (needed or not, the six steps); `src/remoteAccountsRound.ts`,
  `src/remoteWslRound.ts`, `src/remoteAccountsHost.ts`; their tests; `terminalAccountsScenario.ts`.

| # | What must be shown (RED first) |
|---|---|
| E6S3-T1 | Needed: `jq` missing and a tool `unreadable no-jq` → yes; both tools absent → no; the word `none` → no; `jq` found → no |
| E6S3-T2 | The order: read, ready, `begin` with the spawn inside, `end`, read again; one call at a time; a second read only after `installed` or `present`, and only after the door said `yes` again |
| E6S3-T3 | **Zero install calls**, by ticks and by presses, with 20 places that lack `jq`: with no install "yes"; for a refused host; while the round is stopped; with `wsl` refused; in a test run with no test door |
| E6S3-T4 | The timer never calls twice: with a row of each state, three rounds make 0 calls; a press makes exactly 1 |
| E6S3-T5 | The argv, word for word, ssh and WSL. A word that is not one of the two throws. WSL: the `-u` word is the place's user; **`-u root` with an install word only for `(distro, root)`**; no `HOME` and no `WSLENV`; **a distro that is not in the running list read before `begin` gets no `begin`, no row and no call** |
| E6S3-T6 | 180 s and 4 KiB. A stop or a withdrawal during the call does not kill it; its outcome is in the record; no read follows |
| E6S3-T7 | A call that did not reach is `not-reached` with the runner's sentence; a reply that cannot be read is `unreadable`; the log line holds closed words and one number |
| E6S3-T8 | A press while a round runs is refused with its sentence |
| E6S3-T9 | The install call awaits no door of its own: with the real `begin`, a withdrawal that lands before the last look gives 0 spawns on both edges (finding 5) |
| E6S3-H1 | Host scenario, the call replaced through the test door, the "yes" given through the keeper: one terminal, a place without `jq` → one install call, then a second read; the model holds the accounts, the record one row; the folder is put back |

- **Done when.** T1 to T9 green, T3, T5 and T9 seen RED; H1 green in CI's real-editor job.

### E6.S4 — The tab

`model: opus` (the scenarios and the brief, from § M9) · coder: sonnet · review: opus. Depends on S2 and
S3. Serves R10.

- **Files.** `src/remoteAccountsTable.ts`, `src/remoteAccountsGroups.ts`, `src/remoteAccountsPage.ts`,
  `src/remoteAccountsMessages.ts`, `src/remoteAccountsRun.ts`, `src/remoteAccountsPanel.ts`,
  `notification-sites.json`; their tests.

| # | What must be shown (RED first) |
|---|---|
| E6S4-T1 | The model: the foot line in its three states; each sentence of § M9; the heading holds the sentence of the place's NEWEST row; the section holds every row and a line for each file put aside; **a test fails if a text of an install reply is part of a command or a sentence** |
| E6S4-T2 | The messages: `allow-install` only raises the modal; `withdraw-install` acts at once; `install-again` for a place the model does not hold, or whose heading is not `jq-missing`, is dropped |
| E6S4-T3 | The page, by RUNNING it: each button posts its message and no other; a place name with markup in it is the text of a node, also in the section; a place's update keeps the section open |
| E6S4-T4 | The notice: one for `installed`, none for another outcome, and none from a tick that installed nothing |

- **Done when.** T1 to T4 green, T1 and T3 seen RED; the log scan covers the new modules.

### E6.S5 — The documents

`model: opus` (the scenarios and the brief, from this section) · coder: sonnet · review: opus. Last.

- **Files.** The help in five languages; `SECURITY.md`: the three texts that say that the extension only
  reads are changed openly (finding F2 lists them), and it gains the install "yes" with the words of § M2
  — **`jq` and the packages it needs; a package it needs can be updated; nothing is removed** —, the closed
  table, the record, the lock folder, WSL and the residuals of § 9; `research/module_extension.md`,
  `research/module_tests.md` (the container run in the flow catalogue), `research/architecture.md`; the
  release notes; the `POST_DEPLOY.md` question is asked.

- **Tests (RED first).** E6S5-T1: the help census is green, and the install paragraph is in five languages.
  E6S5-T2, a scan: no help text and no line of `SECURITY.md` says that CoAI installs nothing, only reads or
  leaves nothing on a host, but in a sentence that names the install "yes" (break-it: one old sentence put
  back). E6S5-T3: `SECURITY.md` names the install "yes" and its two withdrawals, `sudo -n`, the four
  managers, what else can change on the machine, the record, the lock folder and what is not prevented (§ 9).
- **Done when.** T1 to T3 green; the documents say what the head does; `plan-lifecycle.mjs` is clean.

## 7. Build order, and its gates

1. **Gate E6-G1 and E6's plan round, both before any code.** The gate ran on 2026-11-27 (Opus), and round 1
   of the plan round (codex) ran the same day; this text holds their findings (§ 12). The plan round goes on
   until it passes.
2. **S1 and S2** (the owner's answer O2). They touch no machine.
3. **The gate before S3: the facts of R10** (gate finding 6; the parent's entry 5.3). Before any code of S3,
   two things are on record: the VM row of R10 — it is, since 2026-11-27 — and, for R10 (f), whether root of
   the WSL distro has Claude Code or Codex — it is not. With them the lead lists the owner's places that
   would get an install: no `jq`, Claude Code or Codex there, and a login user who is root or has `sudo`
   without a password. **If the list is empty, the work stops after S1 and S2, and the owner is asked.** As
   measured on 2026-11-27 it is empty (§ 1), so that is the answer to expect.
4. **S3, S4, S5.** One pull request or two for the epic; the cap of three open ones holds.
5. The code gate (`review_code`, fed the scope) and the own Opus review for each pull request.
6. **The container run is green** on the head that merges, with its job present.
7. **The owner's rows on ONE VSIX of E6's build** (§ 8). **R18, R12 and R7 are gates.** Then the merge.
8. `review_feature` over the plan's epics; the extension release: the tag pushed alone and its run observed;
   `POST_DEPLOY.md` run; the parent promoted with this document.

## 8. The real-host checks

Run on one VSIX of E6's build. **Owner** = the owner's hands. **Go** = an agent can run it once the owner
gives the go for its exact commands and names the machine. **The machines** (the owner's answer O6): a VM
that the owner can lose, and a distro made for the check, as W9 used one. **From E5:** R10 is recorded for
this PC and for the VM, and R7 passed on 2.1.0 in WSL and on the VM (2026-11-27). **R12 has no result on
2.1.0: its run here stands for 2.1.0 too** (the owner's answer O5; the parent's entry 5.2).

| # | What is run | Pass | Who |
|---|---|---|---|
| R10 (a) | A VM without `jq`, with Claude Code or Codex, reached as root or with `sudo` without a password. Reading is allowed and the install "yes" is NOT given, for 20 minutes. Then *Allow installing* | Before: 0 installs (the VM's package log), and the heading shows the command. After: `jq` is installed once within the next round; the notice and the row are there; the next read shows the accounts; **the package log shows `jq` and the packages it needs, installed or updated, and no other package; no package was removed**; no service restarted; no install call in the next three rounds | Owner (the presses); Go (the logs) |
| R10 (b) | A user with no rights: the default user of the throw-away distro, without `jq`, whose `sudo` asks for a password. (The owner's own distro has `jq` since 2026-11-27) | Nothing is installed; the row says `no-rights`; the heading shows the exact command and the copy gives it; after the owner runs it, the next round reads the accounts | Owner |
| R10 (c) | A reload with a row in each of `installed`, `no-rights` and `started` | No new install call | Owner |
| R10 (d) | A host with no `jq`, no Claude Code and no Codex | It is logged in to, and nothing is installed | Go |
| R10 (e) | **The consent, in place of the setting.** A second place without `jq` is opened later. Then *Withdraw* of the install "yes". Then *Withdraw the permission*. **Then *Allow reading* again** | The question was asked once; the later place is installed with no question. After the first withdrawal: 0 installs in 20 minutes, also after a reload, and the reads go on. After the second: 0 installs and 0 reads. **After *Allow reading* again: the reads go on, and 0 installs, with a place without `jq` open** (O4) | Owner |
| R10 (f) | The throw-away distro. The fact for the gate of § 7, step 3 — if root of the owner's distro has Claude Code or Codex — is recorded before S3 | No install runs as root for the default user's place (the distro's package log). Root's own place is installed on only if root has a tool | Go |
| **R18** | **Gate (K.38).** The install "yes" is withdrawn in window A; A is closed at once; a stale B writes `globalState` and a stale lock holder writes its file; both are reloaded. Then the same with the permission to read withdrawn | 0 installs from the press on; with reading withdrawn, 0 installs and 0 reads. The folder is read back and holds the withdrawal | Owner |
| **R12** (a)–(d) | **Gate**, on E6's build, as the parent's E5 writes it. Its result is the one for 2.1.0 too | As the parent's row | Owner (two real windows) |
| **R7** | **Gate.** Two hours and 12 rounds with a live Claude Code, Codex or agy session, **on a place where an install ran** — the VM of R10 (a) | 0 live sessions that ask to sign in again, lose their settings or MCP servers, or stop or restart | Go; on a VM the owner answers the creds dialog |
| H1 | **Only if the gate of § 7, step 3 names a root place of a distro** (gate E6-G1). The throw-away distro: `wsl.exe` is killed in the middle of an install as root | A record: what is left of the manager, of the lock and of the package database. Not run: § M8 and I4 keep the words "not measured with a package manager" | Go |

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
  `apk`, `sudo -n`, `env`, `timeout`, `--no-remove`, `install_weak_deps` and `NEEDRESTART_SUSPEND` do is
  what we ASK. The container cases and R10 observe it; what they cannot observe is a residual below.
- *The three texts that say that the extension only reads.* S5 changes them openly.

**Who gains what** (§ E, read for places):

- *A terminal's name, a file of a workspace, the page:* no "yes". Each "yes" is a press in a modal of the
  extension host; a message of the page names a place and carries no command (§ M9).
- *That Linux user, or root:* nothing. The install runs only where they already are root or have `sudo`
  without a password. A lock folder that they make themselves stops only their own install.
- *Another Linux user of the machine:* nothing by a file they plant: every path is absolute, and the `PATH`
  holds four system folders, **through `sudo` too** (§ M4, rules 3 and 7).
- *Someone on the network, or whoever controls a repository the machine trusts:* what they have when the
  person installs a package by hand — at a time the person did not choose. We add no repository and switch
  no check off. **A key that such a repository names can be accepted by the manager** (`dnf -y` does so; gate
  finding 14), and the question says it.
- *A hostile place:* its reply is one line of closed words. It cannot put a word into a command. It can say
  "no `jq`" on every read; the install is still called once.
- *A stale window, a stale lock holder:* it cannot bring back a withdrawn "yes" (the stamp rule, R18). A row
  it loses costs one more call (I6).
- *A second window that withdraws while this one begins:* § M3, the last look. What is left: I11.

**Residuals.**

- **I1.** The package's own scripts run as root, from whatever repositories the machine is set up with.
- **I2.** A hook of the distribution can restart services. We ask it not to (`NEEDRESTART_SUSPEND=1`, set
  after `sudo`, so `sudo` does not drop it): whether the hook obeys is not observed.
- **I3.** A manager stopped at the deadline can need a repair by the person. The row says to check it.
- **I4.** In WSL a window that closes during an install cuts it (W9e; H1 if it is run); the lock can stay.
- **I5.** A refused `sudo -n true` writes a line in that machine's auth log and, where it is set up so, a
  mail to its administrator: once for a place, and once for each press.
- **I6.** A record that is lost — deleted by a program as the person, started anew, or of a second side —
  gives a place one more install call. Beneath it stand `present` and the lock on the target.
- **I7.** The install "yes" is files on one disk: residual 9 of the parent holds for it in the same way.
- **I8.** **The lock is of one home, not of one machine.** Two Linux users or two aliases of one machine
  have two lock folders; the manager's own lock decides. A home that several machines share (NFS) is ONE
  lock for all of them: an install on one makes the others `busy`. And a third call that takes the lock in
  the instant in which a second call has moved a new lock away and not yet back (§ M4, rule 6) runs beside
  its holder: the manager's own lock decides there too.
- **I9.** Refreshed package lists change what a later upgrade by the person sees. A package that `jq` needs
  can be updated (§ M4).
- **I10.** **A machine can kill the sequence when the ssh session ends**: with `KillUserProcesses=yes` of
  `logind`, the processes of the session are killed when our child ends early. Then the sequence does not go
  on to its deadline as § M6 says: the manager is cut as in WSL (I4), and the lock stays until a press. C10
  cannot see it.
- **I11.** A withdrawal is made in the instant after the last look and before the spawn: the install runs,
  and the withdrawing window says *withdrawn* only when the claim is given back. It is "an install that
  runs": its outcome is recorded, and no later one starts.
- **I12.** `sudo -n true` can pass where our command is not allowed (§ M4, rule 5): `failed exit`, and a
  line in that machine's auth log.

## 10. Growth

- **The record** (`installs.json`): one row, about 200 bytes, for each ATTEMPT: one for a place by the
  timer, and one more for each press. The owner's places: tens of rows. The cap: 1000 rows, 512 KiB. **Kept
  for ever, by decision (§ E, finding 6)**, in the folder of the side; at the cap nothing more is installed
  from this side, and the tab says so. *Interrupted:* the file is the old one or the new one (a rename); a
  `started` row is shown as it is.
- **Records put aside** (`installs-damaged-<stamp>.json`): one for each damaged record that a person's
  *Allow installing* started anew; each as it was found; never replaced or removed by us; **at most 8**, then
  no new record is started until a person moves them away. This is the one place where rows leave the
  record that is shown: the file keeps them, and the tab names it.
- **The install withdrawal files:** as the permission's — under 200 bytes each, one left by each *Allow*, at
  most 8. **The state row** gains one stamp: under 250 bytes in all.
- **On a place, by our script:** the lock folder with one file of about 20 bytes, removed when the sequence
  ends. After a KILL of its shell it stays, and only a press removes it. A call that is killed between its
  `mv` and its next line leaves one folder `….lock.<pid>` of the same size; nothing of ours removes it.
- **On a place, by the install:** the package `jq` and what its manager adds or updates for it, a few
  megabytes, not measured, once for a machine; and refreshed lists. Nobody retires them. The record lists
  them.
- **Children:** one install child at a time for a window, killed at 180 s, 4 KiB. **CI:** the containers are
  named `coai-e6-…`, run with `--rm`, and a sweep by that prefix opens the run. **Log lines and notices:** one
  log line for an install call, one notice for an install.

## 11. The owner's questions, and the answers

**Answered by the owner on 2026-11-27, in one answer: every recommended answer below is taken.** Each
answer is in the text above, and the last line of each question says where. O1, O2, O4 and O5 are entries of
the parent (5.1, 5.3, 5.4, 5.2). O3 and O6 change nothing that was approved.

- **O1 — Is E6 the sixth epic of this plan again, or a plan of its own?** Entry 4.2 moved it "to the next
  release, under a later plan or epic of its own", and counted the quota for five epics: 2 of 2. As the sixth
  epic, Q = ⌈2·6/5⌉ = 3, and the mark of E6.S1 is 3 of 3, as K.38 planned. **Recommended: the sixth epic of
  this plan.** The parent then stays in `todo/` until E6 ships, and its ledger line changes with the mark.
  **ANSWERED: the sixth epic of this plan. The parent's entry 5.1.**
- **O2 — Start before R10's VM row is known?** E6 serves the owner only where a place lacks `jq` and its
  login user is root or has `sudo` without a password. **Recommended: S1 and S2 start now — they touch no
  machine — and the VM row is recorded before the code of S3.** If no place of the owner would get an
  install, the owner decides again. **ANSWERED: yes. The VM row is recorded since; § 7, step 3 is the gate;
  the parent's entry 5.3.**
- **O3 — In WSL, may CoAI install as root of the distro for a user who has no rights?** `wsl.exe -u root`
  needs no password, so it would work on this PC. It is a third thing of ours as root, against decision 24,
  and the question's text would have to say it. **Recommended: no, not in this release.** The command is
  shown, and a distro costs the owner one command. **ANSWERED: no. § M8; decision 24 stands.**
- **O4 — Does *Withdraw the permission* of reading also withdraw the install "yes"?** If not, an *Allow
  reading* months later brings the installs back with no new question. **Recommended: yes — one press makes
  both withdrawals.** **ANSWERED: yes. § M2, E6S2-T12, R10 (e); the parent's entry 5.4.**
- **O5 — R12, and the VM half of R7, had no result on 2.1.0. May their runs on E6's build stand for both?**
  E6's build holds all of 2.1.0's store code. **Recommended: yes; the E5 results then say that 2.1.0 was
  released before them.** **ANSWERED: yes. Since then the VM half of R7 passed on 2.1.0 itself (2026-11-27,
  11:00 UTC, after the release); R12 is still not run. § 8; the parent's entry 5.2.**
- **O6 — The go for the checks on real machines:** which VM for R10 (a) and (d), and a throw-away distro for
  H1. **Recommended: a VM that the owner can lose, and a distro made for the check, as W9 did.**
  **ANSWERED: yes, both. § 4 and § 8. The exact commands still need the owner's go when they are run.**

No consultant was asked before the owner answered.

## 12. Judgements, and what the gate and the plan round changed

- **J1.** One document for stories and spec, by Fable (the status block).
- **J2.** The lock is `~/.coai-install-jq.lock`, not under `~/.cache`: one `mkdir`, and no `~/.cache` is made
  in a home that has none. § E left the path to the spec.
- **J3.** The reply has no word `locked`: the manager's exit code does not say it, and its text stays on the
  target. A held package lock gives `failed exit` or `failed timeout` (C7). § E's test said `failed locked`.
- **J4.** The `PATH` has no `/usr/local`: on some systems a group can write there. It is set again after
  `sudo`, which would put its own in its place.
- **J5.** `PATH`, `DEBIAN_FRONTEND=noninteractive` and `NEEDRESTART_SUSPEND=1`, through `env`, after `sudo`.
  Asked; C4 sees that they arrive; what the hook does with the last is not observed.
- **J6.** The record is a file under the claim, not a `globalState` key: a stale window that writes
  `globalState` could put an old record back (entry 1.2), and the "once" guard would go with it.
- **J7.** Two operand words: a press on an old lock has its own word, and the script checks the 200 s itself.
- **J8.** "One `timeout` for the sequence" is built as one deadline, with `timeout -k 10 <left>` for each
  command, inside `sudo`.
- **J9.** ANY row stops the timer, `not-reached` too. **J10.** A record that cannot be read is put aside only
  by *Allow installing* after a *Withdraw*. **J11.** A press is refused while a round runs: no second queue.

**Against the parent.** J3, J6 and J7 differ from words of § E, and K.38 names only the consent and the WSL
place as what is revised when the epic starts. So each is an entry of the parent: 5.6, 5.5 and 5.7. So are
the two that the reviews brought: 5.8 (what else can change on the machine) and 5.9 (a row for each
attempt). The owner approved epoch 6 on this text.

**Gate E6-G1 (Opus, 2026-11-27), finding by finding.** Taken as the judge wrote them: 1 (the parent's
ledger), 2, 4, 6, 7 (the parent and `todo/README.md`), 8, 10, 12, 13, 16, 17, 18, 19, 20, 21. Taken with a
change:

- **3 and 14.** For `yum` and `apk` we set no option against a removal; § M4 says that C1 sees it. The weak
  dependencies are turned off for `dnf` only: whether `yum` knows that option is not verified.
- **5.** The last look and the spawn are the end of `begin`, under the claim, not a door at a later spawn:
  then nothing is awaited between them, and a withdrawal cannot say *withdrawn* in between (§ M3). The
  running list is read before `begin`. So the judge's test "a distro that left the list gives a
  `not-reached` row" is "it gets no `begin`, no row and no call" (E6S3-T5).
- **9.** The rename alone does not close the race: the second call can move the first call's NEW lock. So
  the moved folder is compared with the text judged old, and the lock is put back when it differs (§ M4,
  rule 6).
- **11.** Taken as a case to run (C1, C9 on Alpine). What BusyBox does is from memory and not verified.
- **15.** C7 takes either word; the 60 s of § E stand.

**The judge's cuts.** H1 only if a root place would get an install: taken. C2: cut, C4 holds what it
showed. C11: optional, as it was. **ONE operand word: rejected** — with one word the script would have to
take an old lock away on a timer's call too, and § E's finding 2 says that only a press does.

**E6's plan round, round 1 (codex).** c1: taken, with gate finding 5. c2: taken (§ M4, rule 6: the 2 s, and
the pid in `started`). c3: taken, with gate finding 3. c4: taken (§ M3; the parent's entry 5.9). c5: taken —
stamped names, never replaced, at most 8, named in the tab; **the rows of a damaged file are not shown: they
cannot be read.**

**Open:** the script is not written and its draft has not run; BusyBox's `timeout`; H1; the fact of R10 (f);
a `sudo` that runs its command in a terminal of its own; `yum`; the plan round has not passed.

## 13. Definition of Done — epic E6

- [ ] The owner answered § 11 (done, 2026-11-27). Gate E6-G1 ran and E6's plan round passed on this
      document, before any code.
- [ ] **The gate of § 7, step 3 has a recorded answer before any code of S3**: the places of the owner that
      would get an install, or "none" and the owner's word on it.
- [ ] Every test of § 6 is green, and C1 and C3 to C10 on CI's Linux runner; each "never" test was seen RED
      once with its message. The sha256 of the script is in § M4 and in its test.
- [ ] **Nothing is installed without the install "yes"**, and the permission to read does not give it. A
      withdrawal stops every later install, also against a stale window and a stale lock holder (R18), and
      *Withdraw the permission* withdraws both (O4).
- [ ] **One install at a time for a place, one deadline, and a record of up to 1000 rows from which no row
      goes** (§ E's findings 1, 2 and 6). Where the user has no rights nothing is run. No setting was built.
- [ ] **The question and `SECURITY.md` say what can change on a machine**: `jq` and the packages it needs; a
      package it needs can be updated; nothing is removed.
- [ ] No test and no agent installed anything on a real machine. `npm test` never gave the script a legal word.
- [ ] The code gate and the own Opus review ran for each pull request, and every finding is resolved.
- [ ] R10 (a) to (f) have a recorded result, and H1 has one or the reason why it was not run; **R18, R12 and
      R7 passed on E6's build**, before the merge.
- [ ] `SECURITY.md` and the help say what the head does; `plan-lifecycle.mjs` is clean.
