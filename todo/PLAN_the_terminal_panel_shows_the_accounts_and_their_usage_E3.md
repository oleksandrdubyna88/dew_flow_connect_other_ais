# PLAN — E3 stories: WSL on this PC, and revision 2 of the script

> Status: **plan only, nothing implemented yet, 2026-11-26.** The stories of epic E3 of
> [PLAN_the_terminal_panel_shows_the_accounts_and_their_usage.md](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage.md)
> (its `## Epics` § *E3* and § *E4*, §§ 2, 4 and 5 of *The design*, *Security*, the *Test plan*, the *Definition of
> Done* and the *Merge conditions*), read on the plan's branch at `f5f3362b` — epoch 4, one change (4.1); the copy
> of the parent in this branch is older. Written on `feat/terminal-accounts-e3`, cut from the E2 branch at
> `8407c6bc`, together with its two specs, both by Fable:
> [the WSL road](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E3_S1_spec.md) (E3.S1, the security
> row) and [revision 2 of the script](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E3_S2_spec.md)
> (E3.S2, the cornerstone). **This document was written by Fable too, not by Opus as `subagent-models.md` asks
> for a stories document: the lead's choice, for the owner's deadline (§ 10, N1).** A stories document keeps no
> change ledger: a change to this epic is an entry in the parent. **§ 10 names what the owner said on
> 2026-11-26 that the parent does not hold yet; the planner records it there, not here.** It takes up the todo
> file T46 ([PLAN_the_usage_mode_stays_under_the_30_s_ceiling.md](PLAN_the_usage_mode_stays_under_the_30_s_ceiling.md)):
> the commit of E3.S2 that makes its tests green deletes that file and its README row.
>
> Scope: `src_vs_code` only, plus `SECURITY.md`, `research/` and the help. Five stories: S2 the script, its
> parser and the join (cornerstone); S1 the WSL road (security spec); S3 places; S5 the usage on (the parent's
> E4, one constant); S4 the documents.

## 1. Goal, before any solution

The owner at the real window of 2.0.901 (the parent's entry 2.2): a `Creds: wsl / …` terminal is this PC's own
WSL, and *"wsl must read every WSL on my PC, all accounts"*. Today the scope host `wsl` gets no call and its
block says so (E2). And the script that every place runs cannot have its usage read turned on: it searches the
`PATH` it was given, its usage mode reads Codex and starts `agy` a second time, and its worst path is 31 s
against a ceiling of 30 s (T46). When E3 is done:

1. **A `Creds: wsl / …` terminal in a Windows window shows every running distro of this PC and every Linux
   user in it, root among them**, each read as that user and each a reading of its own. A distro that is not
   running is listed and never started.
2. **Nothing that another user can write is started as root**, and what WSL itself does for a read — the login
   session — is said in words, not hidden.
3. **The script is revision 2**: its own `PATH`; a usage call that reads Claude only and runs the usage command
   only for the account it was sent for; each mode at least 2 s under the ceiling, held by a test.
4. **The Claude windows show numbers** (S5): the usage is read every round, as W6 selected and W7 allowed.

## 2. What exists

E2's § 2 holds for the code (`origin/main` at `401bbf0a`; this branch has no product code of E2 yet). What E3
reads beyond it is in the specs: the S1 spec, § W0 (W9 and the launcher), and the S2 spec, § R2.0 (the script,
the parser and the round, with lines). From E2 this epic uses, by name: `sessions.may('round', host)` (C2),
`tableModel` and `blockOf` (C4), the messages of the page (C5), the round under the permission (C6).

## 3. Conventions every story follows

E2's § 3 holds, with these differences:

- **Worktree.** `coai-accounts-e3`, branch `feat/terminal-accounts-e3`. It merges the E2 branch — or main, once
  E2 is merged — before the first code commit: **E3's code stands on E2's** (the parent's *Depends on*).
- **Models.** S1 and S2: spec by **Fable**, reviewed by **Opus** before the code (gate E3-G1, § 6). S3, S4 and
  S5: this document. Code, tests and fixes: **sonnet**. Review of every story: **opus**.
- **Test ids** are `E3S<story>-T<n>` and `E3S<story>-H<n>`.
- **No test and no agent starts `wsl.exe`.** In `ExtensionMode.Test` the WSL calls are the stub, as the ssh call
  is, until a scenario replaces them. The scripts are run by `sh -s` with false programs in a temporary `HOME`.
- **The owner's rows are run once, at the end, on one VSIX** (§ 7, and § 10, N2).

## 4. Shared contracts

- **K1 — the place (S1 owns).** `Place`, `placeKey` (`ssh:<host>`, `wsl:<distro>/<user>`), the label
  `<distro> · <user>`, `wslPlaceOk`. A reading belongs to a key; the consent, the terminal and the refusal belong
  to the scope host.
- **K2 — the WSL edge (S1 owns).** `runWslList`, `runWslUsers`, `runWslAccounts(place, mode, digest?)`, each
  never rejecting, and the pure `wslRound(seams)`: the order of the S1 spec's § W11 with the door before every
  spawn. Its result: the places with their runs, and the scope host's own facts (the distros, which run, the
  counts past a cap, a failure, the strikes).
- **K3 — the script (S2 owns).** `ACCOUNTS_SCRIPT` of revision 2, `accountsDigest(stdout)`, the argv forms
  `<mode>` and `claude-usage <digest>`, the parser's arm `claude-only`, the states `unsafe` and `skipped
  account-changed`.
- **K4 — the join (S2 owns).** `joined(first, firstKey, second, place)` and `usageMiss`; `AccountsCall` is
  `(place, mode, digest?)`. Until S3 a place is a host.
- **K5 — the round over places (S3 owns).** The memory is a map by `placeKey`. The round walks the scope hosts
  in its rotated order; an ssh host is one place; `wsl` on Windows is `wslRound`. One usage call for each
  account in a round, whatever kind of place answers first.
- **K6 — the policy (S5 owns).** `USAGE_POLICY = { kind: 'every-round' }`.

## 5. Stories

### E3.S2 — The scripts, revision 2 (CORNERSTONE)

`model: fable` (the spec) · coder: sonnet · review: opus · areas (a) and (b). Depends on E2's model (three
sentences) and round. **First.** It has no owner row of its own; it is what R16, R9a and S5's rows stand on.

- **Goal.** One new pinned text with one new sha256, the parser that reads a reply by the mode that was sent,
  and the round that joins a usage reply to the first reply instead of replacing it.
- **Files, scenarios, break-its, brief:** the
  [S2 spec](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E3_S2_spec.md) — §§ R2.2 to R2.8, R2.11
  (E3S2-T1…T24), R2.12, R2.13. The UID-0 runs E3S1-T20…T26 are tests of this text and land with it.
- **Done when.** The spec's Definition of Done; the todo file of T46 and its README row are deleted.

### E3.S1 — The WSL road (SECURITY SPEC)

`model: fable` (the spec, the security row, outside the quota) · coder: sonnet · review: opus · area (e).
Depends on S2 for the digest rule of the argv only; its other files are its own, so it may be coded beside S2.
Serves **R13**, **R17**, **R9a**.

- **Goal.** One module that calls `wsl.exe` as the ssh edge calls `ssh`, and no other way: the lists, the users
  of a running distro, the accounts of each user as that user.
- **Files, scenarios, break-its, brief:** the
  [S1 spec](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E3_S1_spec.md) — §§ W3 to W13, W14
  (E3S1-T1…T19 here; T20…T26 with S2), W15, W17.
- **Done when.** The spec's Definition of Done.

### E3.S3 — Places

`model: fable` (this section; N1) · coder: sonnet · review: opus · areas (c) and (d). Depends on S1, S2 and
E2's S2, S3 and S4. Serves **R13**, **R16**, **R17**, **R9a**.

- **Goal.** The round reads places, not hosts, and the tab shows the WSL places — in the `wsl` block and in the
  three groupings.
- **Files.** `src/remoteAccountsRound.ts` (K5: the memory by key; `wslRound` in the walk; `liveOnly` by scope
  host), `src/remoteAccountsHost.ts` (the real WSL calls and their stub in a test run), `src/remoteAccountsTable.ts`
  (the `wsl` block, the WSL places in the groupings, `tookMs`), the page of E2.S3 (the block drawn; one place's
  update), the tests, `terminalAccountsScenario.ts`.
- **What changes.**
  - **The memory** is keyed by `placeKey`. Two users of a distro never overwrite each other; root's reading is
    never a user's. A place's entry goes when: its scope host's last terminal closes, the scope host is refused,
    the round is stopped, the permission is withdrawn; and for a WSL place also when a round's list no longer
    names its distro or a users reply no longer names its user. A place whose call failed keeps its older
    reading with that read's time. A distro that is only *not running* keeps nothing: it is never shown with
    numbers (residual 12).
  - **The usage rule over places.** `claudeAccountKey` takes the place's key where it took the host. The first
    place of the round that answers with an account whose usage is due gets the usage call; an ssh host and a
    WSL user with one account share one read, and both say *shared across 2 hosts*.
  - **The `wsl` block of the model.** Heading *wsl — this PC* with the count of distros and the scope host's
    result; under it the line S13 (the login session), always; for each distro a line with its name and its
    state (S4, S5, S6 of the S1 spec, § W13), and for each user a table headed `<distro> · <user>` with the rows
    of E2's model. The counts past a cap and the lines not understood (S11, S12). In a window that is not on
    Windows: S1 and nothing else. **Root:** a root place with no tool has no line; a root read that failed has
    one; a tool refused by the check has the row *not read: `<path>` is writable by another user*.
  - **The groupings (entry 4.1).** A WSL place is a place like an ssh host: in *Claude account* under its
    account, or in *no Claude account*; in *User* one row, named `<distro> · <user>`.
  - **`tookMs`**: the model holds, for each place, how long its last call took; the hover of the age says it
    beside the exact time. It is how R9a is read (§ 7).
  - **`copy-install <place>`** names a place key; the host checks it against its model, as E2 does for a host.
  - E2's sentence *WSL on this PC is not read by this build* is removed.
- **Kept.** One call at a time, awaited; the rotation; 10 minutes from the END of a round; the door and the
  round's checks before every call and before every result; results in memory only; the log gate.

| # | What must be shown (RED first) |
|---|---|
| E3S3-T1 | Two users of one distro are two readings and neither overwrites the other; root's is a third |
| E3S3-T2 | An ssh host and a WSL user with one account: ONE usage call in the round; both places say *shared across 2 hosts* with the same figures |
| E3S3-T3 | `wsl` refused in flight: the place's result is dropped, and every `wsl:` entry leaves the memory. With `wsl` refused before the round: **no call to `wsl.exe` at all** through the round, the list calls among them |
| E3S3-T4 | `wsl` takes one turn in the rotation; its calls are one at a time and awaited; the summary's `k of n` counts places |
| E3S3-T5 | Retirement, each cause of the list above; a failed place keeps its older table with its time; a distro that stopped keeps no table |
| E3S3-T6 | The `wsl` block of the model: the heading and its count; S13 always; S4, S5 and S6; S11 and S12; S1 in a window that is not on Windows; S2 |
| E3S3-T7 | Root: no tool → no line; a failed read → a line; an `unsafe` tool → its row with the path, and the place's other tools are rows |
| E3S3-T8 | **The count over the groupings**: every place and every tool reading of an input that mixes ssh hosts and WSL places is in each of the three groupings; a WSL place is `<distro> · <user>` in *User* |
| E3S3-T9 | The page, by running it: the `wsl` block is drawn; one place's update changes that place's table only; a distro's or a user's text with markup in it is the text of a node |
| E3S3-T10 | `copy-install` for a WSL place copies the fixed string of its manager word; a key the model does not hold is dropped |
| E3S3-T11 | The hover of an age says the exact time and how long the read took |
| E3S3-T12 | The sentence *WSL on this PC is not read by this build* is in no source file; in a test run the WSL calls are the stub |
| E3S3-H1 | Host scenario, the WSL seam replaced through the test door: one `Creds: wsl / s` terminal, two distros of which one is stopped, two users and root → the tab's model holds three places and the line of the stopped distro |

- **Done when.** E3S3-T1…T12 green, T2, T3 and T8 seen RED with their break-its; E3S3-H1 green in CI's
  real-editor job.

### E3.S5 — The Claude usage, on (the parent's E4)

`model: fable` (the S2 spec, § R2.9) · coder: the main session or sonnet (one constant) · review: opus. Depends
on **S2 on main** (the parent's E4), and on W6 and W7 recorded with W7 passed — both are recorded, and W7 passed.
Serves **R3**, **R4**, **R2**, **R9b**.

- **Files.** `src/remoteAccountsRound.ts` (`USAGE_POLICY`), its tests, the model's test for the sentence that
  leaves.
- **Tests.** E3S5-T1…T3 of the S2 spec, § R2.9.
- **Where it may merge.** In E3's second pull request, AFTER S2 has merged as its own (§ 6): then revision 2 is
  on main when this constant merges, as the parent's E4 asks. In ONE pull request with S2 it would not be, and
  that is a change of the parent (§ 10, N3).
- **Done when.** The tests are green and the pull request names W6 and W7 with their results.

### E3.S4 — The documents

`model: fable` (this section) · coder: sonnet · review: opus. Depends on S1, S2, S3 and S5 in the branch. Serves
merge condition 6.

- **Files.** The help in five languages (the accounts article: WSL on this PC — every running distro, each
  user, root; a stopped distro is not started; the login session; root's tools are started as root and what is
  checked before; the usage is read every round); `SECURITY.md` (the parent's appendix, § K.32: the WSL road,
  who runs as whom, root's own place and what is started as root, the check and its residual 13, the login
  session of a read, interop; and what W6 and W7 showed); `research/module_extension.md`,
  `research/module_tests.md`, `research/architecture.md`; the release notes drafted in E2's § 12, extended.
- **Not edited.** `src_vs_code/CHANGELOG.md` and `POST_DEPLOY.md`, as in E2.

| # | What must be shown (RED first) |
|---|---|
| E3S4-T1 | The help census is green; the WSL paragraph is in five languages and says the three facts of the login session (the S1 spec, § W9) |
| E3S4-T2 | No help text and no line of `SECURITY.md` says that a WSL read runs no profile, that nothing runs as root, or that WSL is not read by this build (a scan; break-it: one sentence put back) |
| E3S4-T3 | `SECURITY.md` names `wsl.exe`, the users script, root's own place, the check before a start as UID 0 and the login session, and says "read-only" for WSL only with what W9 showed |

- **Done when.** E3S4-T1…T3 green; the documents say what the head does; `plan-lifecycle.mjs` clean.

## 6. Build order, and its gates

1. **Gate E3-G1** — Opus reviews the two specs against the parent and this document, before any code; E3's
   plan round (`review_plan`) reads the three documents. Both read the parent's text on root and on the login
   session, which no round has read (the parent's *Re-review for epoch 4*).
2. **E2's code is on the branch** (a merge of the E2 branch, or of main once E2 is merged). Suite green.
3. **S2**, then its pull request — E3's first. It may merge on merge conditions 1 to 4 and 6 (the parent's E3).
4. **S1**, beside S2 or after it.
5. **S3**, after S1 and S2.
6. **S5**, after S2 is on main.
7. **S4**, last. Then E3's second pull request: S1, S3, S5, S4.
8. The code gate (`review_code`, fed the scope) and the own Opus review for each pull request.

The order of the stories: **S2 → S1 → S3 → S5 → S4.** Two pull requests, so the cap of three open ones holds
with E2's.

## 7. The owner's rows, and which story serves each

Run once, at the end, on one VSIX built from the head that holds E2, E3 and S5 (§ 10, N2).

| Row | What the owner runs (the parent's *E3* and *E4*) | Stories | How |
|---|---|---|---|
| R13 | Two WSL users and root, each signed in to a tool; a second running distro; a stopped one | S1, S3 | The tab; and H1 to H5 of the S1 spec, § W16 |
| R16 | A known sample: one figure the owner knows from the tool itself | S2, S3, S5 | The tab against the tool |
| R17 | *Not this host* on `wsl`: no `wsl.exe` call of this window in 20 minutes, also after a reload | S1, S3 | **The method:** every other VS Code window is closed and no terminal is opened in the 20 minutes. In an elevated PowerShell: `Register-CimIndicationEvent -Query "SELECT * FROM Win32_ProcessStartTrace WHERE ProcessName='wsl.exe'" -SourceIdentifier w`, and after 20 minutes `Get-Event -SourceIdentifier w`. Pass: no event whose parent process is a `Code.exe` |
| R9a | The time of a read, 10 calls for each place, ssh and WSL | S1, S2, S3 | *Refresh now* ten times, a minute apart; the hover of each place's age says the time its read took. None at 30 s, the slowest under 20 s, the medians written down |
| R3 | The numbers, on at least 3 places, against `/usage` there | S5, S2 | As the parent's row |
| R4 | Two places of one account, 6 rounds | S5, S3 | As the parent's row |
| R2 | The logins of a round: 1 on each host, 1 more on ONE host of each account | S5, S2 | The VMs' ssh logs |
| R9b | The time of the usage call, 10 calls | S5 | As R9a |

## 8. Test plan — the parent's rows this epic owns

| Parent row (§ *Test plan*) | Tests |
|---|---|
| Places | E3S3-T1, T2, T3 |
| The WSL road | E3S1-T1…T19; E3S3-T3, T6, T7, T12 |
| The users script | E3S1-T10…T13 |
| Root as a place; the check before a start as UID 0 | E3S1-T5, T20…T26; E3S3-T7 |
| Revision 2 of the script; the join; the account check; T46 | E3S2-T1…T24 |
| The table's model and the page, for WSL and the groupings | E3S3-T6…T11 |
| Host scenario | E3S3-H1 |
| The help | E3S4-T1…T3 |
| The policy (the parent's E4) | E3S5-T1…T3 |

## 9. Growth and security

- **Growth.** The parent's *Growth surfaces* hold as written: readings in memory by place, at most the hosts of
  the open terminals and 32 WSL places; the enumeration, 16 names of distros and 16 users each, replaced every
  round; one `wsl.exe` or `ssh` alive at a time, killed at 30 s, 64 KiB. For `wsl` in a round: 2 list calls,
  one users call for each running distro, one or two calls for each user, and one list call more before each.
  Nothing is written on this side or in a distro by a script of ours. By WSL, on a distro with systemd: one
  login session for each user a call named, 16 at most, until the distro stops. With S5: Claude Code rewrites
  four cache files on a usage read and keeps five backups (W6).
- **Security.** The judgement is the two specs': the S1 spec, §§ W1, W8, W9 and W15; the S2 spec, § R2.10.
  The door is E2's and is asked before every spawn. No story of this epic adds a setting, a file or a message
  that widens what is read.

## 10. Conflicts with the parent, judgements, and open points

**Not in the parent — the owner's words of 2026-11-26, as the lead passed them on.** Each changes a merge
condition, the Definition of Done or the build order, so each is an entry of the parent's ledger, and **epoch 4
already has one change (4.1): by `plan-change-control.md` the next entry is a full stop.** This document does
not record them and does not edit the parent.

- **N2 — one VSIX check at the end.** The parent: the owner's rows of each of E2, E3 and E4 have a recorded
  result on a VSIX built from that epic's head BEFORE it merges (merge condition 5; Q4; the Definition of Done).
  This document lists the rows and when they are run (§ 7); whether a pull request may merge before them is the
  parent's to say.
- **N3 — E4's constant in E3's pull request.** The parent allows it only one way: E3.S2 merges first as its own
  pull request, and the constant merges after it (§ 6). In ONE pull request with S2 it contradicts E4's
  *Depends on* and merge condition "revision 2 of the script is on main".
- **N4 — E6 in the next release.** The parent's merge conditions for the release allow a release without E6;
  its *Build order* (steps 8 to 10) and its Definition of Done put E6 before the one release.
- **N5 — the release within a day.** The parent's release conditions ask for E5's rows, with the gates R7 (two
  hours) and R12, on the head that is tagged. Nothing of E3 changes that.

**Judgements of the specs, for gate E3-G1:**

- **N1.** This document and S3's section were written by Fable; the rule names Opus.
- **N6 — the cornerstone ledger.** E3.S2 is marked `model: fable`, as the parent planned. By `subagent-models.md`
  the parent's line becomes `Cornerstones: 2 of 3 — E1.S2, E3.S2` in the same commit; this branch does not edit
  the parent, so the planner writes it on the plan's branch.
- **N7 — the sums.** 17 s and 22 s for both modes; the parent says 16 s and 21 s for the usage mode (the S2
  spec, § R2.14, 7).
- **N8 — as UID 0 a system tool that is not closed stops the whole place** (`COAI-BAD unsafe`); the parent
  words the rule for `claude` and `agy` (the S1 spec, J10).
- **N9 — two strikes** end the WSL part of a round, and no call is tried twice (the S1 spec, J7 and J8). The
  parent sets the ceiling of a call and no rule for a round of failing calls.
- **N10 — one bad line of a users reply refuses the whole reply** (J5); the parent says such a line "gives no
  place for it".
- **N11 — `tookMs`** in the model and the hover (S3), so that R9a can be read without a log line.
- **N12 — the running list with another exit code counts as "none running"**, until H2 is recorded (the S1
  spec, § W4).

**Open:** H1 to H5 of the S1 spec; the real `jq`, a real UID 0 and a real Claude Code against revision 2 (the
S2 spec, § R2.0).

## 11. Definition of Done — epic E3

- [ ] Gate E3-G1 passed, and E3's plan round read the three documents, before any code.
- [ ] S2: the S2 spec's Definition of Done. S1: the S1 spec's.
- [ ] S3: E3S3-T1…T12 and H1 green, with the RED observations in the pull request.
- [ ] S5: the constant is `every-round`, merged after revision 2 was on main; or it stays `off` and the release
      says why.
- [ ] S4: the documents say what the head does; `SECURITY.md` names the login session and root's place.
- [ ] No test and no agent started `wsl.exe`; nothing was run on a real VM by a test.
- [ ] The code gate and the own Opus review ran for each pull request, and every finding is resolved.
- [ ] The owner's rows R13, R16, R17 and R9a — and R3, R4, R2 and R9b with S5 — have a recorded result, as the
      parent's merge conditions say when § 10's N2 is settled.
- [ ] The todo file of T46 is gone; `plan-lifecycle.mjs` is clean.
