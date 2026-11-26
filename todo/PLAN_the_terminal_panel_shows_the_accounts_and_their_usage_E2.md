# PLAN — E2 stories: one permission, and a table that can be read

> Status: **plan only, nothing implemented yet, 2026-11-26.** The stories of epic E2 of
> [PLAN_the_terminal_panel_shows_the_accounts_and_their_usage.md](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage.md)
> (its `## Epics` § *E2*, §§ 1–3 of *The design*, *Security*, *Test plan*, *Definition of Done*, *Merge conditions* and
> *Re-review for epoch 3*). Written by Opus when the epic started, on `feat/terminal-accounts-e2`, cut from `2df01bb4`
> (the epoch-3 revision); the code it names was read on `origin/main` at `401bbf0a`, which holds E1 (#1047) and the cut
> (#1051). **The owner approved epoch 3 on 2026-11-26**; the planner records that approval in the parent on another
> branch, and the E2 text this document works from does not change with it. A stories document keeps no change ledger:
> a change to this epic is an entry in the parent. **No code of E2.S1 is written before its security spec** (Fable,
> [PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E2_S1_spec.md](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E2_S1_spec.md),
> written 2026-11-26) **and the Opus review of
> that spec** (gate E2-G1, § 6). **Gate E2-G1 ran on 2026-11-26: pass after fixes** — the spec settles every item of
> § 5, E2.S1, and its rows X1–X12 are in §§ 4, 5, 9 and 10 below; one BLOCKING and seven FIX changes to the spec's
> text (§ 6, *Gate E2-G1*) are made by its author before any S1 code. **The author made them (2026-11-26), and the
> four rows that work added, X13–X16, are in §§ 4 and 5 too.** The question's words (W14) name the login session a
> WSL read opens, by the owner's decision 25 of 2026-11-26 (§ 4, C3). The parent's text A1–A8 that the spec names is
> taken as the owner decided (entries 3.1 and 3.2 of the parent, applied on another branch). It also takes up the two todo files T8 and T9, which say they are closed by this document: their findings
> are scenarios of E2.S1 here, and the commit of E2.S1 that makes them green deletes both files and their rows.
>
> Scope: `src_vs_code` only, plus `SECURITY.md`, `research/` and the help. Five stories, as the parent cuts them:
> S1 the permission (security spec), S2 the table's model (pure), S3 the tab, S4 the round under the permission,
> S5 the documents. No cornerstone is marked here: S1's spec is Fable's by the security row, outside the quota, so the
> parent's ledger line `Cornerstones: 1 of 3 — E1.S2` does not change.

## 1. Goal, before any solution

The owner at the real window of 2.0.901 (the parent's *The goal*): *"I may have 20 hosts in the terminal, and it asks
for each. No. Once: may I read?"* and *"Something was read, but who knows what. It must be readable."* When E2 is done:

1. **Nothing is contacted before ONE explicit "yes"** — by the accounts round or by a chat door (*take the question*,
   *add the question*). The "yes" is asked once, by a person's press, in words that name the later hosts, what is read
   and what is started. It covers every host of an open `Creds: <host> / <session>` terminal, now and later.
2. **A "no" for one host, the Stop and a withdrawal always win**, against a round, a reload, a second window, a stale
   write of `globalState` and a stale lock holder that wakes up and renames its old file.
3. **A per-host "yes" of an older build allows nothing**, and the per-host question is gone. A person upgrading from
   0.87.0 is asked the one question once more.
4. **The icon opens a tab with a table, by host**, that the owner reads without help: each host's latest read with its
   clock time, one row for each tool with the account and the two windows as "% left" with the reset, a reason for
   every value that is not there; the ages stay true while the tab is open.
5. In this epic `wsl` gets no call of any kind; its block says why (E3 reads WSL).

The silent failures this epic must not ship (each is a test that was seen RED):

- **A login before the permission or after a withdrawal** — one more line in a VM's ssh log, nothing on screen. The
  forms: a person's "yes" of 0.87.0 still honoured by a chat door; a stale `refused.json` renamed over a withdrawal; a
  stale window's whole-object write of `globalState` dropping the withdrawal's copy; a spawn of a press that was in
  flight (T9).
- **A question raised by a timer.** The timer never asks a person.
- **Markup from a VM.** Any value of a reply, of a terminal's name or of a failure sentence that becomes an element of
  the page.
- **A wrong number that looks right.** A missing value drawn as `0%`; an age frozen at the time the tab was drawn; a
  Codex window in the wrong column.
- **A cure that does not work** (T8): the sentence "your answer repairs the list" while the list is whole and a change
  waits unsaved.

## 2. What exists — verified on `origin/main` at `401bbf0a`, 2026-11-26

Paths under `src_vs_code/` unless they start with `research/`, `todo/`, `.github/` or are a file of the repository root.

| Fact | Where |
|---|---|
| The refusals file: version 1, `gen`, `by`, `refused`, `stop`; **other keys are kept through a write and not read** | `src/remoteRefusals.ts:43-55` `RefusalsFile`, `:168` `refusalsOf` (spreads `raw`) |
| Its changes, markers and the side as read; the store's read and its one writer | `src/remoteRefusals.ts:61-71` `RefusalChange`, `:74-80` `Waiting`/`Unsure`, `:83-85` `RefusalsRead`, `:349-376` `RefusalsDisk`/`RefusalsStore`, `:525` `refusalsStore` |
| The disk over one folder; the file and the marker prefix named once, by the host half | `src/remoteRefusalsDisk.ts:45-50` `DiskNames`, `:142` `refusalsDiskOver`; `src/remoteSessionHost.ts:44-45` `REFUSED_FILE = 'refused.json'`, `MARKER_PREFIX = 'unsure-'` |
| The folder: `globalStorageUri/terminal-hosts`, named once | `shell/modules/chat/register.ts:124-125` |
| The row, the four consents, the round's admissions (`not-asked`, `full`, `read.how`, `allowedNow`) | `src/remoteHosts.ts:15-22` `HostAnswer`, `:32` `Consent`, `:53-60` `Admission` |
| The one consent function; the cut's constant and the rule that uses it | `src/remoteHosts.ts:127` `consentOf`, `:150` `AUTOMATIC_ALLOW = 'off'`, `:153` `autoCounts`, `:160` `yesOf` |
| The automatic "yes" written by the merge, and the cap rule that drops one | `src/remoteHosts.ts:290` `automatic`, `:314` (`auto:` of `APPLY`), `:254` `fitted` |
| The keeper: the round's door and the session door; a waiting marker answers `damaged` for other hosts (T8) | `src/remoteKeeper.ts:15-32` `KeeperSeams` (`allowNew`, `automatic`), `:357` `admit`, `:368-372` `decide`, `:382` `automatic`, `:397` `consent`, `:405` `waitingConsent` |
| The session road: the consent read ONCE for a press; later spawns re-check only the name (T9); the `damaged` door says W6 | `src/remoteRead.ts:272-298` `RoadSeams`, `:305` `CALLS_MAX = 3`, `:313` `asked`, `:329-334` `DOORS`, `:336` `consented`, `:341` `stillThere`, `:348` `readOnce`, `:426` `remoteRoad` |
| The per-host question `read-remote-session`, the hosts list, the setting's two readers, the keeper's binding, the test door | `src/remoteSessionHost.ts:54-66` `askQuestion`, `:97-130` `RemoteSessions`, `:243` `askHostOf`, `:320-321`, `:359-381` `keeperOf` (`:367` `allowNew`), `:384` `bindRemoteSessions`, `:405-462` `RemoteSessionsTestDoor`/`testDoorOf` |
| The words W1–W13, the setting's name inside them | `src/remoteHostsSaid.ts:19` `ASKED_DETAIL`, `:21` `KEY`, `:45-104` `HOSTS_SAID` |
| The round: `ACCOUNTS_READING = 'off'`, its sentence, `noRound`, the timer's lock | `src/remoteAccountsRound.ts:42-52`, `:416-424` `ROUND_OF`/`runRound`, `:502-510` `timerFor`/`timerLocked`; `USAGE_POLICY` `off` `:39` (E4's) |
| The round's seams and its door call; `allowedNow` | `src/remoteAccountsRound.ts:168-184` `RoundSeams`, `:357-376` `hostStep`, `:186-194` `RoundResult` |
| The state row: `toldUtc`, `lastAutoNoticeUtc`, `stoppedUtc`; other keys pass | `src/remoteAccountsRound.ts:29` `STATE_KEY`, `:581-609`, `:611` `StateChange`, `:639` `stateMerge` |
| The window's round, the cut's two checks, the QuickPick, the two notices, the test door | `src/remoteAccountsHost.ts:33-58`, `:140`, `:195`, `:363-376` `bindTerminalAccounts`, `:389-415` `allowedNotice`/`everyTenNotice`, `:459` `AccountsList`, `:540` `openList` |
| The rows of the QuickPick; what the model keeps: `forList`, `INSTALL_JQ`, the sentences | `src/remoteAccountsRows.ts:26` `forList`, `:38` `INSTALL_JQ`, `:46-59`, `:64-69` `Row`, `:426` `accountsRows` (the cut's `ROWS_OF[reading]`), `:453-537` notices and presses |
| The word rules the page will run | `src/usageWords.ts:19` `percentLeft`, `:30` `minutesLeft`, `:48` `resetText`, `:86` `resetsIn`; `src/gaugeWords.ts:92` `agoWords` |
| The setting: manifest, key constant, safety list, user-layer read, ignored layer, export refusal | `package.json:637-641`; `src/modelKeys.ts:27` `ALLOW_NEW_HOSTS_KEY`, `:41` `SAFETY_KEYS`, `:157` `allowNewHostsFrom`, `:160` `ignoredLayerOf`; `src/sideConfig.ts:69` `allowsNewHosts`, `:72` `ignoredLayerFor`; `src/configTransfer.ts:43` (`NEVER_TRANSFERRED`) |
| An imported key the manifest does not declare is refused *unknown to this build* | `src/configTransfer.ts:257-263` `settingRefusal` |
| The command and its row; the chat module's ids | `package.json:288`, `:1046`; `shell/ids.ts:43`, `:99`; `shell/modules/chat/register.ts:127`; the test doors `shell/modules/chat/index.ts:130-133` |
| The pattern of a tab: one `WebviewPanel` per window, reveal, find bar, kept while hidden, no local roots | `src/roundsLogPanel.ts:93-125` |
| Every panel host pushes the text controls, and the census lists the eight hosts exactly | `src/test/textControls.test.ts:105-134` |
| Every webview owner disposes; every panel has the find bar | `src/test/theLogRefusesToOpen.test.ts:116-131`; `src/test/panelsAreSearchable.test.ts:137` |
| A page is run by a shared DOM shim; a bundled page is checked as it ships | `src/test/pageScriptHarness.ts:204` `runPageHtml`; `src/test/bundledPage.test.ts` |
| The clipboard, one copier | `src/copyText.ts:203` `textCopier` |
| The one door for a message; the notice census and its population | `src/notify.ts:340` `notifyAndAsk`; `notification-sites.json`; `src/test/notificationSites.test.mjs:302` `PLACES_THIS_SPEAKS = 179` |
| The help census of settings | `src/test/helpCoverage.test.ts:419` `SETTING_ALIAS` (the key at `:472`), `:882`, `:931-965` |
| The safety list's test; the setting's own test file | `src/test/modelKeys.test.ts:67-68`; `src/test/terminalAccountsSetting.test.ts` |
| The cut's tests and scenarios | `src/test/remoteAccountsCut.test.ts` (CUT-T1…T3), `src/test/remoteHostsCut.test.ts` (CUT-H1…H3, CUT-D2…D5); `src/test/host/terminalAccountsScenario.ts`, `terminalHostsScenario.ts:140-148` (A1-H2), `scenarios.ts:995` |
| Host modules excluded from coverage | `.github/workflows/sonarcloud.yml:274`; `src/test/sonarExclusions.test.ts` |
| The documents | `SECURITY.md:171` (*The extension's outward ssh calls*); `research/module_extension.md:13993` (E1's section) |
| **Found at gate E2-G1** — readers of what S1 retires that are not S1's files | `ALLOW_NEW_HOSTS_KEY`: `src/remoteAccountsHost.ts:4`, `:379` (`SETTING`, in the two notices S4 deletes) (X6). W10: `src/remoteAccountsRows.ts:391` `ignoredRows` (X7). `Admission`: `src/remoteAccountsRound.ts:57`, `:173`, `:310`, `:364-376`; `src/remoteAccountsRows.ts:302-334` `ADMISSION_SAID` (X5). The key's text `coai.terminalAccountsAllowNewHosts`: the five help files (`helpContent.ts:260`, `:621`, `:625` and their four translations), which `helpCoverage.test.ts:882`, `:931`, `:945`, `:961-965` require until S5 |
| **Found at gate E2-G1** — tests that call the keeper's retired doors | `keeper.admit` / `keeper.consent`: `src/test/remoteHosts.test.ts` (102 calls), `remoteHostsReview.test.ts` (14), `remoteAccountsState.test.ts:34`, `:115`, `:128`, `:207` (the round's seams), `remoteRead.test.ts` (13), `remoteHostsCut.test.ts` (deleted), `test/host/terminalHostsScenario.ts:111-150`; the setting's wiring asserts `remoteHostsWiring.test.ts:73-75`, `:83`; `modelKeys.test.ts:67-68`; `modelKeysAreReadOnce.test.ts:50` |

## 3. Conventions every story follows

E1's § 3 holds, with these differences:

- **Worktree.** `coai-accounts-e2`, branch `feat/terminal-accounts-e2`. It merges `origin/main` before the first code
  commit (the branch was cut before the cut merged).
- **Models.** S1: spec by **Fable** (security row), reviewed by **Opus** (gate E2-G1) before its code. S2–S5: spec by
  **Opus** (this document). Code, tests and fixes: **sonnet**. Review of every story: **opus**. Fable refused on a limit →
  Opus, and the summary says so.
- **Test ids** are `E2S<story>-T<n>` (unit) and `E2S<story>-H<n>` (host scenario), so they meet neither E1's `A1-T1`
  nor the terminal-door plan's `S2-T10`.
- **Test-first.** Each scenario below is written RED before its code (a compile error counts for a new module), then
  GREEN, then RED again with its guarded line broken, then GREEN. The pull request lists each observation. A test
  that holds a "never" (no call, no element, no ask) names the production line whose removal turns it red.
- **Pure first.** Every decision lives in a pure module with seams. Host code goes only into
  `src/remoteSessionHost.ts`, `src/remoteAccountsHost.ts` and ONE new host file, `src/remoteAccountsPanel.ts` (S3), with
  its row in `sonarcloud.yml:274` and `sonarExclusions.test.ts` in the commit that creates it.
- **The `remote` prefix** for every new module that touches a reply, a run or a log, so `remoteLogScan.test.ts` covers
  it from its first commit.
- **Census gates in the commit that moves them**: `notification-sites.json` regenerated and `PLACES_THIS_SPEAKS` moved with
  a dated comment naming the codes added and removed; the help census (`helpCoverage.test.ts`); the text-controls
  census (`textControls.test.ts:131`).
- **A webview page is tested by RUNNING it** (`.agents/PROJECT.md`, *A webview page is tested by RUNNING it*). A source
  assertion is allowed only for the CSP, the nonce and a value appearing escaped.
- **Nothing runs on a real VM** in a test. In `ExtensionMode.Test` the round never arms itself and the call is the stub
  until a scenario replaces it (`remoteAccountsRound.ts` `timerFor`, `callFor`) — that lock stays when the cut's lock goes.

## 4. Shared contracts

The names are the contract between the stories; the S1 spec may rename a member, and then this section changes in the
same commit. **Gate E2-G1 (2026-11-26) wrote the S1 spec's names and rules in here** (its rows X1–X12, marked;
X13–X16, added when the spec took the gate's changes, are marked the same way).

- **C1 — the permission (S1 owns; the S1 spec, §§ P3–P7, fixes it).**
  - In `refused.json`, a record of the side: `grant: { utc, stamp }`, a stamp being a whole number from 1 to 10¹².
    Written only by the file's one writer, under its claim, by a person's *Allow*. **A malformed `grant` is no grant,
    not a damaged file**, and is kept through writes as it is; `regain` writes no grant; a `mend` drops it (spec J5,
    § P3). ONE reader, `grantOf`, in the NEW pure `src/remotePermission.ts` (spec J9).
  - **A withdrawal is a file of its own**, `withdrawn-<stamp>.json` beside `refused.json` in `terminal-hosts/`
    (`^withdrawn-([1-9][0-9]{0,12})\.json$`, text `{ "v": 1, "stamp", "utc" }`, read cap 256 bytes), made by an
    exclusive create with no claim, as a marker is made. **A name with the prefix that fits nothing else is an
    unreadable withdrawal.** No writer of `refused.json` ever renames over one, and the sweep of beside-files never
    removes one. There is no marker kind for a withdrawal (spec J2).
  - **The stamp rule** (`inForce`, ONE pure function). The permission is in force only when the grant's stamp is
    HIGHER than the stamp of every withdrawal: every withdrawal file of the folder, a withdrawal this window holds
    unsaved, and the withdrawal stamp copied into the state row of `globalState`. A withdrawal file that cannot be
    read, a copy that is not a stamp, and more than 16 withdrawal names in the folder (X12) each count against the
    grant. An *Allow* writes the grant one above its floor (the readable withdrawals, the copy, the file's own grant);
    the merger refuses a grant-write whose stamp is not above the floor. **A withdrawal takes one above the highest
    stamp it sees** (the parent's A6).
  - **An *Allow*** (X1, spec § P6 and J1): removes the withdrawal files it cannot read and the ones below the floor,
    and leaves ONE readable file, the floor `withdrawn-<grant − 1>.json`, which it makes when it is not there. It
    goes above only the withdrawals that were there when it began; one that appears later ends it as *not given*
    (W16). It never takes the lock over; past a lock that hangs, *Save now* frees it (spec J3).
  - **Pruning** (X11): past 8 files, a withdrawal first removes the LOWEST readable stamp — never the highest, never a
    file that cannot be read; when nothing may go, no file is made and the unreadable ones already withdraw.
  - **The copy** (X2): the state row's `withdrawnStamp`. Written at once on the *Withdraw* press, before the file is
    asked; **copied in** (a file in this side's folder) at every read when it is above the folder; **copied out**
    (raised) at every read when the folder is above it; replaced only by a person's *Allow* when it is not a stamp.
    **The copy crosses sides on purpose** (K5): a withdrawal stops every side that sees it; a grant is never copied,
    so an *Allow* on one side allows nothing on another.
  - A withdrawal the disk did not take is held by its window and is in the copy at once; while it waits the door
    answers `waiting` for every host that is not refused (X10), in this window and in every window of the side that
    has seen the copy.
- **C2 — the one door (S1 owns).** ONE function of the keeper (X3): **`HostKeeper.may(who: 'round' | 'session', host):
  Promise<DoorAnswer>`**, bound as `sessions.may`; `admit`, `consent`, `admitHost` and `askHost` are gone. Asked by the
  round before every call and before every result, and by the session road before EVERY spawn. Its answers, and
  **the first that holds is the answer** (spec § P8): `no` (the host is refused); `damaged` (the list, the file or
  the folder cannot be read); `waiting` (a refusal, a Stop or a withdrawal is not saved yet on this side);
  `no-permission`; and, for the round only, `stopped`; else `yes`. The table of the parent's § 3:

  | Who asks | `yes` only when |
  |---|---|
  | The round | the folder can be read, AND nothing waits unsaved, AND the permission is in force, AND the scope host is not refused, AND the round is not stopped |
  | The session read of a press | the folder can be read, AND nothing waits unsaved, AND the permission is in force, AND the host is not refused |

  A per-host "yes" row — a person's or `how: 'auto'` — is never an answer. The door never asks a person.
- **C3 — the question (S1 owns the function and its words; S3 and S1's road call it).** (X4, spec § P10.)
  **`sessions.askPermission(): Promise<PermissionAsked>`** (`'given' | 'not-now' | { kind: 'not-kept', said }`): ONE
  modal through `notifyAndAsk`, code `read-accounts-permission`, buttons *Allow reading* and *Not now* (W14); no modal
  when the permission is in force; *Not now* writes nothing. Called by: *Allow reading…* in the tab (S3), the first
  row of *CoAI: hosts read over ssh* (S1), and the first door of a chat-door press while there is no permission
  (S1) — that press reads nothing. **`sessions.askPermissionFirst()`** for the icon's press (S3): it asks only when
  the state row holds no `askedUtc` and the permission is not in force, and writes `askedUtc` BEFORE the modal; a
  row that is lost asks once more. **`sessions.withdraw(): Promise<Withdrew>`**: a person's press, narrows at once;
  `Withdrew` is `'withdrawn' | { kind: 'unwritten', why: 'busy' | 'disk' | 'lost' }`. **`why: 'busy'`** (X14, the
  spec's B1, § P7, step 9): the withdrawal's file is made, but its read-back is not final while another window holds
  the claim; it is shown as every `unwritten` is — the door says `waiting`, W9 with *Save now* (S3 shows it).
  **`sessions.permission(): Promise<Permission>`**, `{ given: utc } | 'none' | 'waiting'`, from a fresh read of the
  disk, for the tab — a file that cannot be read is `'none'`, and each host's door says `damaged`. An *Allow* that was
  not kept says W16, and for `unwritten` raises the notice `accounts-permission-not-kept` with *Save now* (spec J4).
  **The words of W14** (the spec's § P12 is their source, E2S1-T14 holds them; owner decision 25, 2026-11-26, made
  them name the login session of a WSL read, and decision 24 root): title *Allow CoAI to read the hosts of your Creds
  terminals?*; detail *“Allow reading” lets CoAI read every host of an open “Creds: <host> / <session>” terminal with
  no further question — the ones open now and the ones you open later. It logs in over ssh with your own keys every
  10 minutes while one is open. In a Windows window with a “Creds: wsl / …” terminal it also reads every Linux user
  of each running WSL distro, root too; on a distro with systemd this opens a login session that runs the user’s
  profile. It reads which of Claude Code, Codex and Antigravity are there, their accounts and usage; claude and agy,
  as found there, are started (root’s as root), and Claude Code rewrites its cache files. Take or add the question
  reads that terminal’s Claude session. CoAI itself installs and writes nothing there. To stop: “Not this host”,
  “Stop the round”, or “Withdraw the permission” for all reads. The permission is for <side> only; another side asks
  its own.* The words cover what the later epics read (the spec's J10), so E3 does not ask again for the WSL read.
- **C4 — the model (S2 owns).** `tableModel(input): TableModel` and `blockOf(model, scopeHost)`, pure,
  JSON-serialisable, deterministic. Every time in it is an absolute instant (ms since the epoch), never an age. Every
  text that came from a VM, a distro or a terminal's name went through `forList` (and so `plainValue`) once. A host
  without `jq` carries its closed manager word, never a command. Its parts: the toolbar (permission, stop, the last
  round's end and `k of n`, or `reading <host>… i of n`), and one block for each scope host of the live `Creds:`
  terminals in code-unit order (heading kind and instant; rows `Tool | Account | 5-hour window | Weekly window`; an
  older table with its instant after a failure).
- **C5 — the page and its messages (S3 owns).** Page → host, closed names only: `ready`, `refresh`, `stop`, `start`,
  `allow`, `withdraw`, `save-now`, `refuse <scope host>`, `read-again <scope host>`, `copy-install <place>`. Host → page:
  `model` (the whole), `block` (one scope host's part). The host checks every name and every argument against its own
  model and drops what it does not know. `allow` and `read-again` only raise S1's modal questions (`allow` →
  `sessions.askPermission()`; `read-again` → the change question of that host, whose *Read this host again* is the
  one button that lifts a refusal); `refuse`, `stop`, `withdraw` (→ `sessions.withdraw()`) narrow at once. The
  clipboard is written by the host with `INSTALL_JQ[word]` of that place, nothing else.
- **C6 — the round (S4 owns).** No timer and no call while the permission is not in force; the first round at once
  when it is given; then E1's rhythm. The door is `sessions.may('round', host)` (X3), whose `no-permission` ends the
  round and whose `waiting` and `damaged` skip every host. `landed(scopeHost)` after each place, so the panel posts
  that block.

## 5. Stories

Build order and its gates: § 6.

### E2.S1 — The permission (SECURITY SPEC)

`model: fable` — **spec: Fable,
`PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E2_S1_spec.md`** (security judgement, area (c), the
security row of `subagent-models.md`, outside the quota). Coder: sonnet · review: opus (of the spec first, gate E2-G1;
then of the code). Depends on nothing in this epic; S3 and S4 depend on it. Serves the owner's rows **R14** and **R8**.

**Why Fable.** It is authorisation: who is contacted with the person's keys, and what keeps a "no" and a withdrawal.

- **Scope.** NEW pure `src/remotePermission.ts` (the stamp rule `inForce`, `grantOf`, the withdrawal file's grammar
  and parser, the door's and the question's types, `permissionAsker`; spec J9); `src/remoteRefusals.ts` (the grant record; the stamp rule; the grant-write that refuses a low stamp;
  `regain` carrying the grant unchanged); `src/remoteRefusalsDisk.ts` (the withdrawal files: exclusive create, list,
  read, remove; their prefix named by the host half; the sweep never touches them; `lockNow`, the read of the lock as
  it is); **ONE new export of `src/chatStoreLock.ts`** — its reader of a lock as it is, with no claim, that the disk's
  `lockNow` reads through (X15; spec § P4, J11, N5): it makes, takes over and removes nothing, and no function or rule
  of the lock that is there changes; `src/remoteKeeper.ts` (the one
  door of C2; `waiting`; `allow`, `withdraw`, `permission`; the copy of the withdrawal stamp; `allowNew` and `automatic`
  out of `KeeperSeams`); `src/remoteHosts.ts` (`consentOf` without the per-host "yes"; `Consent` and `Admission`
  narrowed to C2); `src/remoteRead.ts` (the door before EVERY spawn; `no-permission` asks the one question and reads
  nothing; `waiting` has its own sentence); `src/remoteSessionHost.ts` (`askPermission`, `withdraw`, the hosts list's
  first row, the per-host question out, the test door for the grant and the withdrawal files);
  `src/remoteHostsSaid.ts` (the new words; the setting's words out); the state row's two fields of C1 and C3 in
  `src/remoteAccountsRound.ts` (`stateMerge`); the setting out (`package.json`, `src/modelKeys.ts`,
  `src/configTransfer.ts`, `src/sideConfig.ts`); `notification-sites.json`; the tests and the host scenarios that set
  and restore the store.
- **Not in scope.** The round's timer and its notice (S4); the tab and its messages (S3); the model (S2); the help
  paragraph, `SECURITY.md` and the module documents (S5) — but the help sentence that its own census needs is S1's.
- **Removed, with the file and the symbol.** The automatic "yes": `HostChange` kind `auto`, `automatic` and the `auto:`
  arm of `APPLY` (`remoteHosts.ts:290`, `:314`), the eviction of automatic rows in `fitted` (`:254`), `AutomaticAllow`,
  **`AUTOMATIC_ALLOW`** and `autoCounts` (`:143-153`), `consentOf`'s `allowNew` and `automatic` parameters (`:127`), the
  `yesOf` rule for an automatic row (`:160`); the keeper's `allowNew` and `automatic` seams, `decide`'s automatic branch,
  `automatic` and `afterAuto` (`remoteKeeper.ts:24-27`, `:368-394`), the keeper's `admit` and `consent` (the door is
  `may`), `Admission`'s `not-asked`, `full` (for a new
  host), `read.how` and `allowedNow` (**not by S1**: the first exception below); `waitingConsent`'s `damaged` (`:405`, T8); the per-host question `askQuestion`
  with its code `read-remote-session` and its *Read* button (`remoteSessionHost.ts:54-66`), `askHostOf` (`:243`), the
  road's `ask` seam and `asked` door (`remoteRead.ts:281`, `:313`, `:330`); `allowsNewHosts` and `ignoredLayer` of
  `RemoteSessions` (`:119-120`, `:320-321`); W2 `ASKED_DETAIL`, `KEY` (**not by S1**: W10 is built from it, the third
  exception below; X13), `forgetSaid`'s "allowed again by itself",
  `rowDescription`'s automatic arms, `emptyOn`, W7's sentence on the setting, W10 `ignoredWorkspace`/`ignoredFolder`
  (`remoteHostsSaid.ts:19-104`; W10 **by S2**, the third exception below); the setting `coai.terminalAccountsAllowNewHosts` (`package.json:637-641`),
  `ALLOW_NEW_HOSTS_KEY`, its place in `SAFETY_KEYS`, `allowNewHostsFrom` and `ignoredLayerOf` (`modelKeys.ts:27`, `:41`,
  `:157`, `:160`), `allowsNewHosts` and `ignoredLayerFor` (`sideConfig.ts:69-73`), its
  `NEVER_TRANSFERRED` row (`configTransfer.ts:43`: an old export that holds it is then refused *unknown to this build*,
  `:263`), its `SETTING_ALIAS` (`helpCoverage.test.ts:472`). Tests deleted with what they hold:
  `terminalAccountsSetting.test.ts`; the automatic-row and eviction cases of `remoteHosts.test.ts`; `CUT-H1…H3` and
  `CUT-D1…D5` of `remoteHostsCut.test.ts` (CUT-D1, the session road on an automatic row for both purposes, too: X8),
  **each assertion carried into E2S1-T10 or T18 before the file goes**; A1-H2
  (`terminalHostsScenario.ts:140-148`, `scenarios.ts:995`).
- **Three exceptions to *Removed*, with the bridge** (gate E2-G1; the spec's X5–X7 and § P17, *The bridge*). They are
  read by code that is S2's, S3's or S4's, so S1 leaves them, each with a doc line naming the story that deletes it,
  and the compile stays green with S1 alone:
  - **`Admission` is not deleted by S1** (X5): the round (`remoteAccountsRound.ts:57`, `:173`, `:310`, `:364-376`) and
    the rows (`remoteAccountsRows.ts:302-334`) read its kinds. S1 adds the pure `admissionOf(answer: DoorAnswer):
    Admission` beside it, and `remoteAccountsHost.ts:140`'s second arm becomes
    `this.sessions.may('round', host).then(admissionOf)` (its first arm, the cut's lock, is untouched). S4 deletes the
    type, `admissionOf` and that line; S2 deletes `ADMISSION_SAID`.
  - **The setting's key stays in ONE line of `remoteAccountsHost.ts`** (X6): `ALLOW_NEW_HOSTS_KEY` is imported there
    (`:4`) for `SETTING` (`:379`), the text of the two notices S4 deletes; S1 drops the import and writes the string
    literal there. The key's text also stays in the five help files until S5 rewrites them (gate E2-G1, F1).
  - **W10 stays** (X7): `ignoredWorkspace` and `ignoredFolder` are read by `remoteAccountsRows.ts:391` (`ignoredRows`);
    S2 deletes them with the row. **So does the constant `KEY` of `remoteHostsSaid.ts:21`** (X13, the gate's F1), from
    which W10's two sentences are built (`:94-95`): it is the THIRD place where the key's text stays in `src/`, and
    E2S1-T17's scan allows that one line by name, with a companion that only W10's two sentences use it. The commit
    that deletes W10 (with `ignoredRows`: § 5, E2.S2, *When each goes*) deletes `KEY` and removes that allowance.
  - The other bridge lines: `remoteAccountsHost.ts:342-343` become `ignored: undefined`, `allowNew: false`, and `:445`
    becomes `ask: (host) => sessions.changeHost(host)` — S3 deletes both with the QuickPick. **While
    `ACCOUNTS_READING` is `off` no build executes the bridge**; S1 does not touch that constant.
  - **The tests that call the retired doors move with S1** (gate E2-G1, F3): `keeper.admit`/`keeper.consent` become
    `keeper.may(…)` (and `may('round', h).then(admissionOf)` where a test hands the round its seams,
    `remoteAccountsState.test.ts`), and every fixture whose assertion expects a host to be read gives the permission
    first through the keeper's `allow()`. Each E1 test whose expectation changed is named in the pull request.
- **Kept.** The refusals file, its lock that is never taken over by age, `gen`, `by`, the read-back, the markers, the
  copy-in of "no" rows, `saveWaiting` and the torn cure (E1.S1 as built); `claimFile`; the list's parser, which still
  KNOWS a "yes" row and `how: 'auto'` so a list that holds one is not damaged; `commit` and `commitKey`; *Not this
  host*, *Forget* and the change question for a refusal; the Stop, which stops the round and not the session read.
- **T8 and T9, taken up.** `waiting` is an answer of its own and the session door and the tab say: *A “Not this
  host”, a stop or a withdrawal is not saved yet on this side, so nothing was read. Choose “Save now” — the first row
  of CoAI: hosts read over ssh.* (W12, with gate E2-G1's F7.) The road asks
  the door before EVERY spawn of a press (`readOnce`), and a later spawn never asks a person: any answer but `yes`
  stops it with that answer's sentence. The two todo files
  ([PLAN_a_waiting_marker_has_its_own_door_sentence.md](PLAN_a_waiting_marker_has_its_own_door_sentence.md),
  [PLAN_a_press_rereads_the_consent_before_each_spawn.md](PLAN_a_press_rereads_the_consent_before_each_spawn.md))
  and their rows in `todo/README.md` are deleted in the commit that turns E2S1-T12 and T13 green.
- **What the spec must settle** — **all twelve settled** by the spec's § P2 (gate E2-G1): 1 → `may` and the order of
  § P8; 2 → a malformed grant is no grant (J5); 3 → § P4; 4 → an *Allow* removes what it cannot read, after the floor
  file (§ P6); 5 → steps 0–11 of § P6, pruning with no lock (§ P7); 6 → the copy crosses sides and only narrows
  (§ P5; the parent's A1–A3); 7 → no marker kind (J2); 8 → `regain` writes no grant; 9 → W1–W18 (§ P12); 10 → three
  choices, each only where it does something, no `mend` "yes" (§ P10, J6); 11 → § P13; 12 → residuals 1–19 (§ P14).
  The list as it was asked (the conflicts found in the code are § 10):
  1. The door's exact signature and closed answer set (C2), and what each caller does with each answer.
  2. The grant's JSON and its parse: a malformed `grant` — damage of the whole file, or no grant?
  3. The withdrawal file: the name grammar (digits, padding, a name with the prefix that fits no grammar), its content,
     its size cap; the stamp a withdrawal takes; an exclusive create that meets an existing name.
  4. **An unreadable withdrawal file counts as the highest stamp: how does an *Allow* ever get above it?** (§ 10, K4.)
  5. The order of an *Allow* under the claim (read the withdrawals, write the grant, read back, remove the older
     files) and what a withdrawal made between those steps does; the pruning past 8, under which lock.
  6. **The `globalState` copy crosses sides; the parent's residual 10 says a withdrawal on one side leaves the other's
     permission standing.** Which holds? (§ 10, K5.)
  7. `waiting` for a withdrawal: a new marker kind beside `refuse` and `stop`, or the copy alone; *Save now* for it;
     a torn marker.
  8. `regain` of a file that went back: the grant rides with its stamp and is never raised.
  9. The words: the question's title and detail (everything a "yes" covers, as the parent's § 3 lists it), the
     `no-permission` and `waiting` sentences at the session door, an *Allow* the file did not take, a withdrawal not
     saved, the hosts list's first row; each with a W-number and its test.
  10. The hosts list after the per-host "yes": what a stored "yes" row shows, which three choices a host has
      (*Not this host*, *Read this host again*, *Forget*?), and what a `mend` "yes" over a damaged list means now.
  11. The test door: how a scenario sets and restores the grant and the withdrawal files (raw writes in test mode only).
  12. The residuals, renumbered with the parent's 9–12 and the file's eight.
- **Scenarios the spec must contain at least** (it may split or add; it keeps these ids or maps them). The spec keeps
  T1–T18 and H1 and adds **T19** (K4: a withdrawal file that cannot be read, and an *Allow* that removes it), **T20**
  (a withdrawal wins over generated histories, and the invariant I1), **T21** (a copy that is not a stamp; the top of
  the count), **T22** (*Save now* frees a hanging lock when nothing waits, J3) and **T23** (the list after the "yes":
  `how: 'auto'` rows go at the next write; the cap) — its § P15 is the full table, its § P16 the break-its R1–R33 (X16: R31 is
  the gate's B1 — a withdrawal's read-back that ignores a held claim; R32 is F6 — the floor file made after the
  removals; R33 holds the start-again sentence of the spec's § P6, step 9).

| # | What must be shown (RED first) |
|---|---|
| E2S1-T1 | The grant record: parsed when whole; a file without one has no permission; a malformed one as the spec decides. **A write by E1's writer keeps a `grant` it does not know** (RED: a writer that rebuilds the object from its known keys) |
| E2S1-T2 | The stamp rule, as a table: no grant; grant 1, no withdrawal → in force; grant 1, withdrawal 1 → not; grant 2, withdrawal 1 → in force; a withdrawal only in the state row's copy → not; a withdrawal file that cannot be read → not |
| E2S1-T3 | *Allow* writes the stamp one above its floor, leaves exactly one withdrawal file (the floor, one below the grant: X1), and is read back; a grant-write whose stamp is not above the newest withdrawal is refused and reported as not given |
| E2S1-T4 | *Withdraw* makes `withdrawn-<stamp>.json` by an exclusive create; no path of the writer of `refused.json` renames over it and the sweep never removes it (over the disk seam's recorded calls); past 8 files the LOWEST readable stamp goes first, never the highest and never a file that cannot be read (X11) |
| E2S1-T5 | **The stale lock holder (the plan round's finding 1).** Withdraw; close the window that saw it; a former holder renames its stale `refused.json`, which holds the grant, over the new one; reload → still withdrawn, **zero calls of either door** |
| E2S1-T6 | **The stale window.** A withdraws; A is disposed; stale B writes the WHOLE `globalState` object (the copy gone); both are built again as after a reload → no call. The same for an *Allow* given after the withdrawal: B's stale write does not take it back |
| E2S1-T7 | A withdrawal the disk did not take: the copy is written BEFORE the file is asked; the door answers `waiting` for every host that is not refused (a refused host answers `no` first: X10) in every window of the side that has seen the copy, on both doors; *Save now* lands it. An *Allow* the file did not take changes nothing and is told |
| E2S1-T8 | The copy: a window that has not read the folder refuses at once from the state row; the next read copies the stamp into the folder; the copy only grows |
| E2S1-T9 | **The one door**, every cell of C2's table for both callers; a companion scan finds no second reader of the grant or the withdrawal files and no caller of a retired door. The Stop stops the round and not the session read |
| E2S1-T10 | **A per-host "yes" allows nothing (the plan round's finding 0).** A person's "yes" for h, an automatic row for g, no permission: a press of *take* or *add* on h asks the ONE question and **runs nothing**; *Not now* → nothing; *Allow* → nothing in that press; the next press reads. After a withdrawal: a press runs nothing. The list's bytes are the same after 100 presses (never written again). Carries CUT-H1, H2, D1 (both purposes: X8), D2–D5 |
| E2S1-T11 | **The upgrade**, row by row: a person's "yes" (0.87.0) not honoured; an automatic "yes" (2.0.901) no answer; a "no" in the file or as a row a refusal; the Stop stays and an *Allow* does not start a stopped round; the setting in a settings file not read. A list holding a "yes" row or a `how: 'auto'` row is not damaged |
| E2S1-T12 | **T8.** A held refusal for h, a press on g → the `waiting` sentence, never W6; a held withdrawal → the same; a damaged list → W6, as today |
| E2S1-T13 | **T9.** A "no" saved during the first spawn of a press → no second spawn; a withdrawal saved then → no second spawn; the answer still `yes` → the fallbacks run (S3-T13's companions stay green); a later spawn never calls the ask seam (a fake ask that throws) |
| E2S1-T14 | The question: raised by the icon's first press and not by its second; a lost state row asks once more; **a tick never asks** (the ask seam throws from a tick); its code, title, detail and two buttons are the spec's words; `read-remote-session` is in no source file and not in `notification-sites.json` |
| E2S1-T15 | The hosts list: the first row is the permission, in its four states (*Allow reading…*; *Withdraw the permission*, with its time; *Save now*; the damage row: W18); a host's choices are the spec's three, **each offered only where it does something** (§ P10's table); no *Read* button for a host; no change can carry `answer: 'yes'` |
| E2S1-T16 | The sides (K5, as the spec settles it): a grant of one side is not one of another and is never copied; a withdrawal on one side stops another side at its next read, which copies it into its own folder; an *Allow* on one side does not give the other back |
| E2S1-T17 | The setting is gone: not in the manifest; `SAFETY_KEYS` is `['kimiAllowNetworkSignIn']`; not in `NEVER_TRANSFERRED`, and an imported file that holds it is refused *unknown to this build*; no source reads it (the key-read scan of `modelKeysAreReadOnce.test.ts`); a user layer holding `true` writes no row and allows no host. The key's text is in `src/` only in the ONE bridge line of `remoteAccountsHost.ts` (X6, removed by E2S4-T9), in the five help files (removed by E2S5-T2), and in the ONE line of `remoteHostsSaid.ts` that holds the constant `KEY` (`:21`), which W10's two sentences are built from (X13; the scan allows that line by name, a companion holds that only W10 uses it, and the commit that deletes W10 removes the allowance) |
| E2S1-T18 | Retired code is gone and stays gone: no `kind: 'auto'` change can be built (a type test), no `AUTOMATIC_ALLOW` or `autoCounts` exists, and the parser still reads `how: 'auto'` |
| E2S1-H1 | Host scenario (CI's real-editor job): through the real `globalState` and the real folder — no permission → the door answers `no-permission`; an *Allow* through the keeper; a withdrawal → a `withdrawn-*.json` on the disk and the door answers `no-permission`; everything restored in a `finally` |

- **Done when.** The spec is written and reviewed by Opus (E2-G1) BEFORE any S1 code, and the gate's BLOCKING and FIX
  changes (§ 6) are in the spec; E2S1-T1…T23 green, each break-it of the spec's § P16 seen red with its message;
  E2S1-H1 green in CI's `extension · a real editor` job, read from that job; the replaced E1 tests named in
  the pull request; `notification-sites.json` regenerated, **180 sites after S1** (179 − `read-remote-session` +
  `read-accounts-permission` + `accounts-permission-not-kept`: X9); `npm run compile` exit 0; the suite and lint green.

### E2.S2 — The table's model (pure)

`model: opus` (spec, this section) · coder: sonnet · review: opus · area (d) (absent, signed out, no usage source,
skipped, failed and unreadable are different, and a missing number is never a zero). Depends on nothing in this epic
but C1's `permission()` shape; may start with S1's spec. S3 and S4 depend on it. Serves **R15** and **R5**.

- **Goal.** One pure function makes, from what the window holds, everything the tab shows — so the page only draws.
- **Files.** NEW `src/remoteAccountsTable.ts` (`tableModel`, `blockOf`, the types of C4). `src/remoteAccountsRows.ts`
  reduced to what the model keeps: `forList`, `INSTALL_JQ`, `NO_MANAGER_SAID`, `USAGE_OFF_SAID` and the sentences a
  cell uses. The tests `remoteAccountsTable.test.ts`.
- **Removed.** From `remoteAccountsRows.ts`: `Row`, `HostPress`, `AccountsView`, `accountsRows` and the cut's
  `ROWS_OF[reading]` (`:426`), the QuickPick labels (`HOSTS_LABEL`, separators), `ChooseSeams`, `choose`,
  `copyDecisionFor` (its rule moves to the panel's message check), `noticesDue`, `NoticeSeams`, `tellAfterRound`
  (`:453-537`); **from S1's bridge** (gate E2-G1): `ADMISSION_SAID` and the rules over it (`:302-334`, X5), and
  `ignoredRows` (`:390-391`) with the W10 words `ignoredWorkspace`/`ignoredFolder` of `remoteHostsSaid.ts` (X7), and
  the view's `ignored` and `allowNew` fields. Their tests in `remoteAccountsRows.test.ts` and `remoteAccountsList.test.ts` go, **but for the cases
  that hold a word, a dash or the `jq` table, which are rewritten against the model**. **When each goes** (gate
  E2-G1): `remoteAccountsHost.ts` (S3's file) imports `AccountsView`, `ChooseSeams`, `NoticeSeams`, `Row`,
  `accountsRows`, `choose`, `copyDecisionFor` and `tellAfterRound` (`:13`), so S2 lands its model beside them and
  these, with everything only they use (`ADMISSION_SAID`, `ignoredRows`, W10), are deleted in the commit of S3 (the
  QuickPick) or S4 (the notices: `NoticeSeams`, `tellAfterRound`) that deletes their last caller — each story lands
  with the compile green, as S1's bridge does.
- **Input.** The window's memory (E1's `HostMemory` for each host), the standings, the Stop, the permission (C3:
  `sessions.permission()`, `{ given } | 'none' | 'waiting'`; a host whose door says `damaged` carries W11), the
  live scope hosts, the round's state (running: host and index; ended: end instant, read and asked counts), and
  whether the window runs on Windows (for the `wsl` sentence).
- **The rules** (the parent's § 1): a block for each scope host of the live terminals, in code-unit order; the heading
  kinds *read* (instant), *never read*, *not allowed* (with the reason), *failed* (the runner's own sentence, whole,
  with its instant), *`jq` missing* (the manager word); a tool that is absent has no row, a place with no tool one line;
  the account cell (email and organization; the plan; *shared across N hosts* only when vendor and whole account text
  are equal and known); Codex's *account name not shown by Codex* and its session file's instant on its own row; agy's
  *its account is not read*; a window cell is `left` (a number or `null`) and `resetsAt` (an instant or `null`), the
  number made by `percentLeft` in one place; Claude's single-model weekly windows as sub-lines; a Codex window placed
  by its length (300 → 5-hour, 10 080 → weekly, another named in its cell); usage that was not read spans both window
  cells with its reason; a failed host keeps its older table with that read's instant; `wsl` has no rows in E2 and
  says *WSL on this PC is not read by this build* in a Windows window, *WSL is read from a Windows window only* in
  another.

| # | What must be shown (RED first) |
|---|---|
| E2S2-T1 | By host: one block for each scope host of the live terminals, in code-unit order; none for a host with no live terminal; no `Creds:` terminal → the model's one sentence |
| E2S2-T2 | Each heading kind from its memory and standing, with its instant; a refused host is *not allowed* with its reason and has no rows |
| E2S2-T3 | A tool that is absent has no row; a place with no tool has one line; the version under the tool, or *not known* |
| E2S2-T4 | **Absent is not zero.** A window with no value has `left: null`; a test fails if any path of the model makes `0` from `null`, `undefined` or a missing key (property test over generated readings) |
| E2S2-T5 | A Codex window by its length, each of the three cases; the Codex row carries its session file's instant; the model sub-lines of Claude's weekly cell |
| E2S2-T6 | *Shared across N*: two places of one known account → both say 2 and show the same figures; an unknown account is never shared; two accounts with equal numbers are not one |
| E2S2-T7 | Every "not read" and "unavailable" reason spans the two cells with its own sentence; **no tool row has two empty window cells without a reason** |
| E2S2-T8 | A failed host keeps its older table with that read's instant, marked older; its failure sentence is whole |
| E2S2-T9 | The toolbar: the last round's end instant and `k of n`; while a round runs, `reading <host>… i of n`; the permission (none, given with its instant, waiting) and the Stop |
| E2S2-T10 | The `jq` case carries the manager word only; the string a copy would write is `INSTALL_JQ[word]`, and **a test fails if any text of a reply is part of it** (replies seeded with a marker) |
| E2S2-T11 | Every VM text went through `forList` once: control and format characters out, cut at its length; `<img src=x onerror=…>`, `$(account)` and 10 KB arrive as plain, cut strings |
| E2S2-T12 | `wsl` in E2: no rows, the sentence for each platform |
| E2S2-T13 | The model survives `JSON.parse(JSON.stringify(m))` unchanged; `blockOf(m, h)` equals the block in `m`; the same input gives the same model |

- **Done when.** E2S2-T1…T13 green with their break-its; compile, suite and lint green at S2's own commit; **after S3
  and S4**, the reduced `remoteAccountsRows.ts` has no export the model does not use.

### E2.S3 — The tab

`model: opus` (spec, this section) · coder: sonnet · review: opus · area (c) for its two widening messages. Depends on
S2 (the model) and, for its wiring, S1 (C3). Its page half may start once S2's types are merged. Serves **R1** and
**R15**, and the question of **R14**.

- **Goal.** The icon opens ONE editor tab that the owner reads without help, whose times stay true, and from which no
  VM text can become markup.
- **Files.** NEW `src/remoteAccountsPage.ts` (pure: the HTML with its CSP and nonce; the page script that builds every
  node from the model by `textContent`; the word rules embedded by their source text, as `roundsLog.ts` embeds its own,
  so there is one copy: `usageWords.ts` and `agoWords`); NEW `src/remoteAccountsMessages.ts` (pure: a page message
  checked against the model → a closed command, or nothing); NEW host `src/remoteAccountsPanel.ts` (the panel; its
  sonar row); `src/remoteAccountsHost.ts` (the command opens the panel; the test door); `src/gaugeWords.ts` only if
  `agoWords` must be made embeddable; the tests.
- **The panel.** On `RoundsLogPanel`'s pattern (`roundsLogPanel.ts:93-125`): viewType `coaiTerminalAccounts`, title
  *AI accounts and usage*, `ViewColumn.Active`; `enableScripts`, `retainContextWhenHidden`, `enableFindWidget`,
  `localResourceRoots: []`; `pushTextControlsTo`; `onDidDispose` releases it. A second press reveals the open tab. **No
  serializer**: after a reload the tab is not restored and the icon opens it again — a restored tab would draw a model
  this window does not have yet. The page says `ready`; the panel answers with the whole model, and with `block` posts
  after each place (C6).
- **The page.** The toolbar; the permission block (without it, one block: what would be read, and *Allow reading…*;
  with it, a last line with its time and *Withdraw the permission*); one block for each host. Ages and *resets in* are
  written by the page from its own clock every 15 s; the clock time of a read is on its heading; a hover shows the
  exact local date and time. Under a width of 560 CSS pixels the four columns stack, each field with its label.
- **The CSP.** `default-src 'none'; script-src 'nonce-…'; style-src 'nonce-…'`; no remote source; no inline handler.
- **Removed.** The QuickPick: `AccountsList`, `openList`, `shownList`, `ListItem`, `BUTTON`, `ITEM_OF`, `itemOf`,
  `shownOf`, `chooseSeamsOf` (`remoteAccountsHost.ts:418-544`); `ShownItem`, `noteShown`, `view`, `ViewExtra` and the
  test door's `lastItems` (`:33-72`) — the door gets `lastModel()` instead. Their scenario in `terminalAccountsScenario.ts`
  is rewritten (E2S3-H1). With them go S1's two bridge lines of the list (`:342-343` `ignored`/`allowNew`, `:445` `ask`).
- **S1's calls it wires** (gate E2-G1, X4): the icon's command calls `sessions.askPermissionFirst()`; the page's
  `allow` calls `sessions.askPermission()` and `withdraw` calls `sessions.withdraw()`; an *Allow* that was not kept
  shows S1's W16 (X1: also *withdrawn while it was being saved*) and the tab's `waiting` state says W12.

| # | What must be shown (RED first) |
|---|---|
| E2S3-T1 | **Run the page** (`runPageHtml`): a model whose values hold `<img src=x onerror=…>`, `$(account)` and 10 KB of text → every one is the text of a node; the count of elements by tag equals what the model ordered. Break-it: one `innerHTML` assignment from data |
| E2S3-T2 | Each button posts its one message and nothing else (a table: button → message and argument) |
| E2S3-T3 | **The owner's observation as a test.** The shim's clock goes on 10 minutes with no message from the host: the heading reads *read 10 min ago*, its clock time is unchanged, a *resets in* went down by 10 minutes. Break-it: the interval removed |
| E2S3-T4 | A `block` post changes that host's block only: the other blocks keep their node identity |
| E2S3-T5 | A hover title on an age and on a reset holds the exact local date and time |
| E2S3-T6 | The narrow width: the stylesheet (`cssRules.ts`) holds the media rule that stacks the columns and shows the labels |
| E2S3-T7 | The bundled page defines every embedded word function (`bundledPage.test.ts`'s check, extended); the page's `resets in` and `% left` words equal `usageWords.ts`'s on its own cases |
| E2S3-T8 | The CSP and the nonce (a source assertion, legitimate here): no source but the nonce, no `http`, no `'unsafe-'` |
| E2S3-T9 | The message check: each closed name with a known argument → its command; an unknown name, a host not in the model, a place with no manager word → nothing; `copy-install` hands the copier exactly `INSTALL_JQ[word]` |
| E2S3-T10 | `allow` and `read-again` change nothing by themselves: with a fake question that answers Escape, no grant is written and no refusal lifted; `refuse`, `stop` and `withdraw` act at once |
| E2S3-T11 | The panel's wiring: one panel; a second open reveals it; the census of `textControls.test.ts` holds nine hosts; `onDidDispose`; the find bar (`panelsAreSearchable.test.ts` finds it) |
| E2S3-T12 | The icon's first press without a permission opens the tab AND asks the one question once (`askPermissionFirst`); the second press only reveals |
| E2S3-T13 | The door census still allows `coai.terminalAccounts` by name (E1's K1 allow-list) |
| E2S3-H1 | Host scenario: the icon opens the tab; a second press shows the same tab; with the call replaced through the test door and the permission given through the keeper, two terminals of two hosts are one call each, and `lastModel()` holds both blocks |

- **Done when.** E2S3-T1…T13 green, T1 and T3 seen RED with their break-its; E2S3-H1 green in CI's real-editor job;
  compile, suite and lint green.

### E2.S4 — The round under the permission

`model: opus` (spec, this section) · coder: sonnet · review: opus · area (c) (who is contacted and WHEN). Depends on S1
(the door) and S2 (the summary's fields); its posts reach the page through S3. Serves **R14**, **R5** and **R6**.

- **Goal.** No timer and no call before the permission; after it, every host that is not refused is read, the later
  ones too, with E1's rhythm; a withdrawal stops it at once.
- **Files.** `src/remoteAccountsRound.ts`, `src/remoteAccountsHost.ts`, `notification-sites.json`, the tests and
  `terminalAccountsScenario.ts`.
- **What changes.** The round asks C2's door, `sessions.may('round', host)` (X3), where it asked `admit`, and takes
  its answers as they are: **S4 deletes S1's bridge** — the type `Admission`, `admissionOf` and the line
  `remoteAccountsHost.ts:140` (X5) — and the line `SETTING` (`:379`) with the two notices (X6). The state row's
  `withdrawnStamp` and `askedUtc` are S1's (`permissionRowOf`, its three `StateChange`s); S4 adds the notice's time
  beside them. `armFor` and `timerFor` arm only
  when the permission is in force; an *Allow* starts a round at once (unless one runs or the round is stopped); after
  that, 30 s after activation when a `Creds:` terminal of a host that is not refused is open, and 10 minutes after the
  end of each round. A withdrawal bumps the generation, clears the timer and the memory at once. The scope host `wsl`
  gets no call in any window and its block says why (E2S2-T12). `landed` names the scope host so the panel posts that
  block. **The one notice**, once for each installation, when a `Creds:` terminal is first seen without the permission:
  *CoAI can show the AI accounts and usage of the hosts of your Creds terminals.*, one button *Open*, which opens the
  tab and decides nothing (code `accounts-can-be-shown`, a new site); its time in the state row.
- **How each constant of the cut ends.** **`ACCOUNTS_READING`** (`remoteAccountsRound.ts:49`) is **deleted, not set to
  `on`**: with it go `AccountsReading`, `READING_OFF_SAID`, `noRound`, `ROUND_OF` and `runRound`'s `reading` parameter
  (`:416-424`), `timerLocked`'s `reading` half (`:502-510`, the test-mode half stays), the host's two checks
  (`remoteAccountsHost.ts:140`, `:195`). A constant that one edit could set back to `off` is a second switch nobody
  presses; the permission is the one gate, and a build with no permission reads nothing by C2. **`AUTOMATIC_ALLOW`**
  (`remoteHosts.ts:150`) is deleted by S1 with the automatic "yes" it switched; it is not set to `on` either.
  `USAGE_POLICY` stays `off` (E4's). CUT-T1…T3 of `remoteAccountsCut.test.ts` become E2S4-T1 and T2 before that file is
  deleted.
- **Removed.** `allowedNotice` and `everyTenNotice` with their codes `accounts-hosts-allowed` and
  `accounts-read-every-ten-minutes` (`remoteAccountsHost.ts:389-415`), `noticeAfter`'s use of them, `RoundResult.allowedNow`
  and `admittedAs`'s count (`remoteAccountsRound.ts:186-236`); the state row's `toldUtc` and `lastAutoNoticeUtc` and
  their `StateChange`s (`:581-627`) — a row that still holds them reads as before, it is not damaged; the A4-T13 tests.
- **Kept.** One call at a time, awaited; the rotation; 10 minutes from the END of a round; the checks before every
  call and before every result; the account first and one usage call for each account; `USAGE_POLICY` `off`; the log
  gate; the latch; *Refresh now*'s 60 s floor; the test-mode locks (`timerFor`'s `test`, `callFor`).

| # | What must be shown (RED first) |
|---|---|
| E2S4-T1 | **The acceptance of entry 2.3.** A store with a person's "yes", an automatic "yes", one refusal and 20 live hosts, `wsl` among them, and NO permission: **zero calls** of any runner and zero door-`yes`, by 100 ticks and by every press (*Refresh now*, opening the tab, *Start*); the timer is not armed. Carries CUT-T1 and T2 |
| E2S4-T2 | After *Allow*: a round starts at once; exactly one first call for each host that is not refused, **none to the refused host, none to `wsl`**; a terminal of a new host opened later is read in the next round with no question |
| E2S4-T3 | A withdrawal during a round: the round is abandoned, its result is not published, the memory is empty and the timer cleared; the next tick calls nothing |
| E2S4-T4 | A stopped round stays stopped through an *Allow*; *Start* after it runs a round within 60 s |
| E2S4-T5 | **The timer never asks**: the question seam throws from any tick; a tick with no permission raises nothing |
| E2S4-T6 | Activation with the permission in force and an eligible terminal: the first round 30 s later; without it, no timer |
| E2S4-T7 | Each place's result posts its block (`landed(host)`), not the whole model, while the round runs; the summary at the end counts the round's places |
| E2S4-T8 | The one notice: shown once when a `Creds:` terminal is first seen without the permission; never again (its time in the row); not shown with the permission; its button opens the tab and writes no grant |
| E2S4-T9 | Retired: `ACCOUNTS_READING`, `READING_OFF_SAID`, `allowedNow`, `Admission`, `admissionOf` and the two notice codes are in no source file and not in `notification-sites.json`; E2S1-T17's allowance of the bridge line of `remoteAccountsHost.ts` is removed (X6); a state row holding `toldUtc` and `lastAutoNoticeUtc` is read, not damaged, and is not written with them again |
| E2S4-T10 | E1's round tests stay green unchanged but for the door's name: the order, the account first, one usage read per account, `USAGE_POLICY` `off`, a result in flight never outlives a Stop, a refusal or a *Forget* |
| E2S4-H1 | Host scenario, the cut's scenario rewritten: before the permission no call, through the real keeper; after an *Allow* through the keeper, with the call replaced, one call for each of two hosts |

- **Done when.** E2S4-T1…T10 green, T1, T2 and T5 seen RED with their break-its; E2S4-H1 green in CI's real-editor job;
  `PLACES_THIS_SPEAKS` moved with a dated comment: S1 leaves 180 (X9: −1 `read-remote-session`, +1
  `read-accounts-permission`, +1 `accounts-permission-not-kept`); S4 moves it to 179 (−2 for the two notices, +1 for
  `accounts-can-be-shown`).

### E2.S5 — The documents

`model: opus` (spec, this section) · coder: sonnet · review: opus. Depends on S1–S4 merged into the branch (it
describes what they built). Serves merge condition 6 and the owner's reading of R14 and R15.

- **Files.** The help in five languages (`src/helpContent.ts`, `helpRu.ts`, `helpUk.ts`, `helpDe.ts`, `helpEs.ts`: the
  accounts article and the remote paragraph rewritten for the one permission, the tab and the withdrawal; the cut's
  *"not in this build"* sentences out; the setting out); `src/test/helpCoverage.test.ts` (the words it looks for);
  `SECURITY.md` *The extension's outward ssh calls* (`:171`): *"No host is contacted before the person's one
  permission"* in place of *"A host is contacted unless the person refused it"*, where the permission is kept, what it
  covers, the withdrawal file and why a stale writer cannot undo it (the writer "claims, writes and reads back", never
  "cannot be lost"), that a withdrawal reaches every side through its copy and a grant only its own (X2), that a
  thing that cannot be read counts as withdrawn, the read caps (16 withdrawal names, 256 bytes each: X12), the tab
  and its CSP, the residuals 1–19 of the S1 spec (§ P14); `research/module_extension.md` (a section for E2 after `:13993`), `research/module_tests.md`,
  `research/architecture.md`; the boundary sentence in
  [research/PLAN_the_chat_opens_from_a_terminal.md](../research/PLAN_the_chat_opens_from_a_terminal.md) (its per-host
  consent is replaced: the parent's *Boundaries*, Q1); § 12 of this document filled from the code as built.
- **Not edited.** `src_vs_code/CHANGELOG.md` (the release is one for the whole plan; its prose is written then) and
  `POST_DEPLOY.md` (E1's question stands; § 12).

| # | What must be shown (RED first) |
|---|---|
| E2S5-T1 | The help census green: the command's title in the English help; no `SETTING_ALIAS` for the retired key; every article in five languages |
| E2S5-T2 | No help text in any language, and no line of `SECURITY.md`, names `coai.terminalAccountsAllowNewHosts`, says a host is allowed by itself, or keeps the cut's *"not in this build"* sentence (a scan; break-it: one sentence put back); E2S1-T17's allowance of the five help files is removed, and `helpCoverage.test.ts:882`, `:931`, `:945`, `:961-965` no longer ask for the key |
| E2S5-T3 | The help's remote paragraph says the one permission, that it covers the hosts opened later, and how to withdraw it, in five languages (the test of `helpCoverage.test.ts:931-965` rewritten for the new words) |

- **Done when.** E2S5-T1…T3 green; the documents say what the head does (merge condition 6); `plan-lifecycle.mjs` clean.

## 6. Build order, and its gates

1. **Merge `origin/main`** into the branch (E1, the cut). Compile and suite green before any story.
2. **The S1 spec** — Fable. Anything it needs outside the parent's text (a second file format, a change of the lock,
   a new kind of call) is recorded in the parent's ledger by the caller and approved BEFORE S1's code. The coder never
   adds one.
3. **Gate E2-G1** — Opus reviews the S1 spec against the parent and against this document; any change to C1–C3 goes
   into § 4 and into the briefs of S3 and S4 in the same commit, before they are coded. **Verdict, 2026-11-26: pass
   after fixes** (below). The rows X1–X12 are in §§ 4, 5, 9 and 10; S1's code starts when the spec's author has made
   B1 and F1–F7 in the spec and the caller has checked them against this list. **Made, and X13–X16 taken in
   (2026-11-26):** X13 in § 5, E2.S1 (*Removed*, *W10 stays*, E2S1-T17); X14 in § 4, C3 (`Withdrew`'s `busy`, which
   S3 shows); X15 in § 5, E2.S1, *Scope*; X16 in § 5, E2.S1, the scenarios' line (R1–R33).

### Gate E2-G1 — the review of the S1 spec (Opus, 2026-11-26)

Read: the spec at `6c9179ad`; the parent in this worktree (its A1–A8 taken as the owner decided, entries 3.1 and 3.2);
the code on `origin/main` at `401bbf0a`. Every symbol of § 5, E2.S1, *Removed* was searched in `src/` and `shell/`;
X5–X8 hold, and two more readers were found (F1, F3). Nothing was compiled or run.

**What holds.** All twelve items are settled (§ 5, E2.S1). The door's one order (§ P8) has an action and a sentence for
each answer and each caller. The grant lives only in the locked file, and nothing in `globalState` can give it (M1,
M4). The stamp rule with the floor file (J1, I1, I2) makes a stale lock holder's rename lose to any later withdrawal
on its side (M3; T5, T20). A torn or hostile file narrows (M5, M6). The upgrade starts with no permission (M7, T11).
K5's reading only narrows across sides. **J3 is safe**: `free()` is E1's `take: 'person'` claim, by the same press
(`saveWaiting`); a former holder's stale rename after it cannot undo a withdrawal (I2) and can only lose a grant or
bring back a refusal (E1's residual 5b). The bridge (§ P17) keeps the production compile green with S1 alone, and is
dead code while `ACCOUNTS_READING` is `off` (`remoteAccountsHost.ts:140`).

**BLOCKING — changed in the spec before any S1 code.**

- **B1. An *Allow* whose window dies after its write outlives a later withdrawal** (M2 is a lock holder that died; V13
  is the copy's lag between windows). § P5 says *"every part of the floor can be seen by any window that withdraws"*;
  the copy cannot (V13). A withdrawal of another window that had not seen the copy yet, made between steps 3 and 5 of
  § P6, takes a stamp below the grant, and its read-back (§ P7, step 9) runs before step 8 and ends. Only step 10 of
  the *Allow* then catches it — and step 10 does not run when that window closes, reloads or crashes after step 8,
  nor is it said what step 10 does when its listing fails. The permission is then in force after the person's last
  *Withdraw*. Spec change: § P5, *What a stamp is taken from*, the sentence *"Nothing that only one window knows is in
  it: every part of the floor can be seen by any window that withdraws, so a withdrawal that is made while an Allow
  runs is never below that Allow's grant."* → *"The copy is in the floor, and another window may not have seen it yet
  (V13): a withdrawal made while an Allow runs can be below that Allow's grant. Step 10 of § P6 and step 9 of § P7
  hold it, and step 9 does not need the Allow's window to live."* § P7, step 9, add: *"A claim is held (its note read
  without a claim): the read-back is not final. It reads back again when the claim is released or its owner is shown
  gone (E1's test, never by age), at most `WITHDRAW_ROUNDS` times; a grant at or above its stamp then → again from 4.
  Until it is final, `pending` stays."* § P6, step 10, add: *"A listing or a read that fails: as a new name — the
  Allow makes `withdrawn-<the grant's stamp>.json`, and answers `withdrawn` (`unsaved` when that fails)."* T20's
  generator gains the *Allow*'s window dying between steps 8 and 10 (its claim's owner gone) with a withdrawal of a
  window that had not seen the copy, and a step-10 listing that fails. Break-it **R31**: *step 9 of § P7 ignores a
  held claim* → T20 red, *an Allow that died after its write outlived a later withdrawal*.

**FIX — small, in the spec, before S1's code.**

- **F1. T17's scan cannot pass at S1.** The key's text `coai.terminalAccountsAllowNewHosts` is in the five help files
  (`helpContent.ts:260`, `:621`, `:625` and the translations), and `helpCoverage.test.ts:882`, `:931`, `:945`,
  `:961-965` REQUIRE it there until S5. § P15, T17: *"the key's text is in `src/` in the ONE bridge line and nowhere
  else (X6)"* → *"… in the ONE bridge line (X6) and in the five help files, which S5 rewrites (E2S5-T2 removes this
  allowance), and nowhere else"*; § P11's table row for the setting: *"the key is in no source"* → *"no source reads
  the key"*.
- **F2. The withdrawal prefix is named once, but the pure parser needs it.** § P4 names `WITHDRAWN_PREFIX` once in
  `remoteSessionHost.ts` (T9's scan), while `withdrawalName(stamp)` and `withdrawalOf(name, text)` in the pure
  `remotePermission.ts` must spell `withdrawn-`. § P4: `withdrawalName(prefix: string, stamp: number)`,
  `withdrawalOf(prefix: string, name: string, text: string | undefined)`, the grammar built from the handed-in prefix
  (as E1's markers take `DiskNames.markerPrefix`).
- **F3. E1's tests cannot stay "unchanged but for the door's name".** With no grant, `may` answers `no-permission`
  where dozens of E1 assertions expect a read (`remoteHosts.test.ts`, 102 door calls; `remoteHostsReview.test.ts`, 14;
  `remoteAccountsState.test.ts:34`, `:115`, `:128`, `:207` hand the round `keeper.admit`). § P15, last paragraph:
  *"E1's tests of the file, the lock, the markers, the copy-in and `regain` stay green and unchanged but for the
  door's name"* → *"… stay green: `keeper.admit`/`keeper.consent` become `keeper.may(…)` (`may('round', h).then(
  admissionOf)` where a test hands the round its seams), and a fixture whose assertion expects a host to be read
  gives the permission first through the keeper's `allow()`. Each test whose expectation changed is named in the
  pull request."* The replaced list gains `remoteHostsWiring.test.ts:73-75` and `:83`, `modelKeys.test.ts:67-68` and
  `modelKeysAreReadOnce.test.ts:50`. § P17's bridge table gains the row *`remoteAccountsState.test.ts`: the round's
  `admit` seam → `may('round', h).then(admissionOf)` — S4, with the round*.
- **F4. An *Allow* that answers `kept` leaves a held withdrawal standing.** § P10 tells the person *given* for
  `kept`, but § P6's keeper drops a held withdrawal on `given` only, and the next read lands it above the grant.
  § P6, *Then the keeper, on `given`: it drops a withdrawal it held unsaved; …* → *"on `given` or `kept`: it first
  drops a withdrawal it held unsaved (the person's later word, in the order of this window's queue); on `given`, the
  row's `{ floor, utc }` …; it answers `given` only when its own `inForce`, with `held` false after that drop, over a
  fresh read …"*. T7 gains the cell: a withdrawal held, the copy erased by a stale write, *Allow* → `kept`, the door
  says `yes`.
- **F5. "What was there when the *Allow* began" is taken when the queue reaches it, not at the press.** A *Withdraw*
  pressed in another window after the *Allow*'s press, whose file lands before step 0, loses. § P6, step 0, add:
  *"Step 3 also counts as new a readable withdrawal whose `utc` is later than the Allow's `utc` (taken at the press),
  whatever listing it was in."* T19 gains that cell.
- **F6. The floor-before-removal order has no break-it.** § P16 gains **R32**: *an Allow removes the unreadable files
  before it makes the floor file* → T19 red, *the old grant was in force between a removal and the floor*.
- **F7. W12 does not say where *Save now* is.** § P12, W12: *"… so nothing was read. Choose “Save now”."* → *"… so
  nothing was read. Choose “Save now” — the first row of CoAI: hosts read over ssh."* (this document's § 5, E2.S1,
  *T8 and T9, taken up*, follows it).

**Applied, 2026-11-26** — by the spec's author, in the commit that adds this line: B1 and F1–F7 are in the spec's text, with N1–N4 and N8 folded in. Where the code or a fix asked for more than the texts above, the spec says so and lists it for this document in its rows X13–X16: F1's allowance also covers the one `KEY` line of `remoteHostsSaid.ts` that W10 is built from (X13); B1 brings the disk read `lockNow` with one export of `chatStoreLock.ts`, `why: 'busy'` for a withdrawal, an order in `saveWaiting` and one start-again sentence in § P6, step 9, with break-it R33 (X14–X16). The caller checks these against this list before S1's code.

**NOTE — no change needed before code; each is for the spec's author or the caller to weigh.**

- N1. The restart at `STAMP_MAX` removes every withdrawal file, so after it a stale `refused.json` with an older,
  higher grant beats a later withdrawal (I1 and I2 do not hold across it); only a hand-made file reaches it — name it
  in residual 7 or 17.
- N2. An *Allow* whose floor is its side's own old grant raises the copy and can withdraw another side with no
  *Withdraw* press; a withdrawal held in another window of the side lands after an *Allow* and takes it back
  silently. Both narrow and cost one press; neither is in § P14's table.
- N3. `low` should stay out of `Committed` (`commit` cannot send a `grant`), or every exhaustive table over
  `Committed` grows.
- N4. `withdraw({ utc, atLeast })` takes one ABOVE `atLeast` (§ P7, step 5): the name says the opposite; `above` reads
  truer.
- N5. Reading a lock's note without a claim (B1) is a read of the disk seam, not a new kind of call or a change of the
  lock's rule (§ 6, step 2).
- N6. W14 is long (about 150 words) and names reads E2 does not do (WSL, the usage: J10, residual 19); the owner
  should know that before R14.
  (Answered 2026-11-26: with the owner's decision 25, W14 was rewritten shorter and names the login session a WSL
  read opens and root's read; its words are in § 4, C3. The owner still sees the words before R14.)
- N7. The parent's entries 3.1 and 3.2 "with no stop": `plan-lifecycle.mjs` is red when the current epoch holds two
  entries and is not `stopped` (`plan-change-control.md`, *The check*). The planner's commit records it as one cause
  (the spec's own reading, A1–A6) or records the stop.
- N8. A withdrawal file is written under 100 bytes; 256 is the READ cap (§ 9's "under 200" stays true).
- N9. Every "RED first" of the spec is read from the code, not observed (its status says so); the coder observes each.
4. **S1 and S2**, in parallel — Sonnet; Opus reviews; Sonnet fixes. Their files are disjoint: S2
   touches `remoteAccountsRows.ts` and a new module, S1 the consent files. S1 lands first.
5. **S3** — the page half may start after S2 lands; its wiring after S1.
6. **S4** — after S1 and S2; it edits the state row after S1 has.
7. **S5** — last.
8. **One pull request for the epic.** The code round gets the scope — this document's §§ 1, 4 and 11 and the S1
   spec's scope — as `planText`. CI green with its checks present; the real-editor job read by its own result, its
   sha in the pull request.
9. **The owner's rows** (§ 7) on a VSIX built from the pull request's head (Q4), each with a recorded result; a row
   that fails is fixed on the same pull request and run again. Then the merge.

## 7. The owner's rows, and which story serves each

| Row | What the owner runs (the parent's *E2*) | Stories |
|---|---|---|
| R1 | The icon: ONE tab, the same tab on a second press, Ctrl+F finds a host, no `Creds:` terminal said | S3, S2 |
| R14 | The permission: 0 logins in 20 minutes before it; the question once; after *Allow* the first login within 60 s and exactly 1 on each host not refused when the round has ended, 0 on the refused one; a chat door before it asks and logs in nowhere; a new host read with no question; *Withdraw* → 0 logins in 20 minutes by either door, also after a reload | S1, S4, S3 |
| R15 | The table: each host's tools, accounts, two windows with "% left" and reset; the Codex figure equals the VM's record; a Claude account says why its usage is not read; 12 minutes untouched and the ages grow; the hover; the narrow tab | S2, S3 |
| R5 | A host that cannot be reached: its failure in full within 30 s; the others read in that round | S2, S4 |
| R6 | Every `Creds:` terminal closed → 0 logins in 20 minutes; *Stop the round* → 0 in 20 minutes, also after a reload; started again → a round within 60 s | S4, S3 |
| R8 | *Take the question* reads with the permission and asks no per-host question; *Not this host* on a heading → 0 logins there in 20 minutes, also after a reload, and the chat door there refuses; *Read this host again* → read in the next round | S1, S3 |

How the owner counts logins (the VMs' ssh logs) is the parent's; nothing in this epic reads them.

## 8. Test plan — the parent's rows this epic owns

| Parent row (§ *Test plan*) | Tests |
|---|---|
| The permission — the acceptance of entry 2.3 | E2S4-T1, T2, T5; E2S1-T14; E2S3-T12 |
| Every door (finding 0) | E2S1-T9, T10 |
| Withdrawal and its races (finding 1) | E2S1-T2…T8, T19…T22; E2S4-T3, T4 |
| The upgrade | E2S1-T11, T23 |
| The session door, T8, T9 | E2S1-T9, T12, T13 |
| The table's model | E2S2-T1…T13 |
| The page, by running it | E2S3-T1…T10 |
| Host scenario | E2S1-H1, E2S3-H1, E2S4-H1 |
| The manifest and the help | E2S1-T17; E2S3-T11, T13; E2S5-T1…T3 |
| *Places*, the WSL road, revision 2 | not E2 (E3) |

Unit and wiring tests run in `npm test`, here and in CI. The host scenarios cannot start on this machine (no
`libnss3`); they run only in CI's `extension · a real editor` job, which is `continue-on-error`, so its own conclusion
is read and written in the pull request, and a red scenario blocks the merge as if it were required.

## 9. Growth and security

- **Growth** (the parent's *Growth surfaces*, made concrete): the grant — one key in `refused.json`, under 100 bytes;
  the withdrawal files — under 200 bytes each (written under 100; read at most 256), one left after each *Allow* (the
  floor), at most 8 before a withdrawal removes the lowest readable stamp; a withdrawal interrupted is the file or no
  file (exclusive create); a file that cannot be read is a withdrawal until a person's *Allow* removes it (§ 10, K4);
  a listing reads at most 16 files, and more names with the prefix count as one unreadable withdrawal, which an
  *Allow* removes, 1024 names in a press (X12). The state row gains the withdrawal stamp's copy, the
  question's time and the notice's time, and loses two fields: under 150 bytes. `coai.terminalHosts` is never written
  by anything automatic; a person's answers only. The tab: one panel per window and one 15 s interval of its page,
  gone with the tab. The memory: as E1, dropped also at a withdrawal.
- **Security.** The judgement of the permission is Fable's (S1 spec). What this document decided inside the parent's
  text: the page assembles no HTML from data and its CSP allows no source but its nonce (E2S3-T1, T8); a page message
  names and never carries, and the two that widen only raise a modal (E2S3-T9, T10); the clipboard gets a fixed string
  of a closed table (E2S2-T10, E2S3-T9); the timer never asks (E2S4-T5); the cut's two constants are deleted rather
  than switched, so no build has an off-switch beside the permission (E2S4-T9, E2S1-T18).

## 10. Judgements, conflicts with the parent, and open points

**Judgements made here inside the parent's text** (no plan change; said so the caller can see them):

- **J1.** Both cut constants are deleted, not switched on (S4, *How each constant of the cut ends*).
- **J2.** The tab has no serializer: it is not restored after a reload (S3).
- **J3.** The page embeds the word rules by their source text, as `roundsLog.ts` does, with the bundled-page guard —
  `localResourceRoots: []` leaves no other way to give the page one copy of them.
- **J4.** The stacking width is 560 CSS pixels (the parent leaves the number to the spec).
- **J5.** `wsl` gets no call in E2 in ANY window, not only on Windows; its sentence differs by platform. The chat
  doors of a `Creds: wsl / …` terminal are not changed (the parent's *What this plan does NOT do*).
- **J6.** The notice's code is `accounts-can-be-shown` and its button *Open*.

**Conflicts found in the code, for the S1 spec** (K-numbers):

| # | The parent says | The code says | What happens |
|---|---|---|---|
| K1 | The setting leaves `src/sideConfig.ts` and `SAFETY_KEYS` | `ignoredLayerOf`, `allowNewHostsFrom` and `ALLOW_NEW_HOSTS_KEY` live in `modelKeys.ts` (`:27`, `:157`, `:160`); **`ALLOW_NEW_HOSTS_KEY` has one more caller, `remoteAccountsHost.ts:4`/`:379`** (X6, found by the spec), and the key's text is in the five help files (gate E2-G1, F1) | S1 removes them with `terminalAccountsSetting.test.ts`; the one line of `remoteAccountsHost.ts` holds the literal until S4; the help until S5 |
| K2 | "A new page with its panel" | The text-controls census holds the eight panel hosts exactly (`textControls.test.ts:131-134`) | S3 adds the ninth in its commit |
| K3 | The hosts list "stays as it is … the three choices for one host", and loses its *Read* | The change question offers *Read*, *Not this host*, *Forget* (W3), and `mend` writes a person's "yes" over a damaged list | **For the spec** (item 10): the three choices and what a `mend` "yes" means now. **Settled** (spec § P10, J6): each choice only where it does something; no *Read*; no `mend` "yes" — over damage *Not this host* or *Read this host again* (`mend` with `forget`) |
| K4 | An unreadable withdrawal file counts as the highest stamp; an *Allow* writes one above the highest and removes all but the newest | Then the unreadable file is the newest, it is kept, and no grant is ever above it | **For the spec** (item 4): e.g. an *Allow* — a person's press — removes the files it cannot read before it writes, under the claim. **Settled** (spec § P6, J1; the parent's A4, A5, entry 3.1): the floor file first, then the unreadable files go, then the grant |
| K5 | The copy of the withdrawal stamp is in `globalState` and copied into the folder at every read; residual 10: a withdrawal on one side leaves another side's permission standing | `globalState` is the profile's: E1's "no" copies reach every side and are copied in there (the E1 stories, § 10, open point 10) | **For the spec** (item 6): a withdrawal reaches every side, or the copy names its side. **Settled** (spec § P5; the parent's A1–A3, entry 3.2): a withdrawal reaches every side that sees its copy; a grant is of its own side and never copied |
| K6 | The state row "remembers that it was asked" and holds the copy of the Stop | `accountsStateOf` checks only its three known fields and lets others pass (`remoteAccountsRound.ts:590-601`) | New fields are added to the check; the two retired ones stay readable (E2S4-T9) |
| K7 | Retire `NEVER_TRANSFERRED`'s row | An import refuses an undeclared key as *unknown to this build* (`configTransfer.ts:263`) | Safe to remove; E2S1-T17 holds the refusal |

**Questions the S1 spec must settle** — the twelve items of § 5, E2.S1, with K3, K4 and K5 above.

**Open, for the people named.**

1. **The owner's rows need a VSIX from the pull request's head** (Q4, approved with epoch 3) — for the caller.
2. **The PR cap.** E2 is one pull request; whether it is over the day's cap is the caller's to check against the
   recorded exceptions before it is opened.
3. **The pre-release VSIX 2.0.901** on the owner's machine wrote automatic rows; E2S1-T11 covers them, and R14 starts
   from that profile.

## 11. Definition of Done — epic E2

- [ ] The S1 spec written by Fable and reviewed by Opus (E2-G1) BEFORE its code; § 4 updated with what it changed; any
      change outside the parent's text recorded in the parent's ledger first.
- [ ] S1–S5 coded to their specs; every scenario green; every break-it seen red, then green, and listed in the pull request.
- [ ] Nothing is contacted before the permission, by either door, whatever is stored (E2S4-T1, E2S1-T10, T11); a
      withdrawal stops both doors at once and survives a stale window and a stale lock holder (E2S1-T5, T6).
- [ ] The automatic allow, `coai.terminalAccountsAllowNewHosts`, its notices, the per-host question and the cut's two
      constants are gone from the code, the manifest and the help (E2S1-T17, T18; E2S4-T9; E2S5-T2).
- [ ] T8 and T9 are green (E2S1-T12, T13), and their two todo files and rows are deleted.
- [ ] The tab: one per window, grouped by host, ages that tick, every value text under the CSP (E2S2, E2S3).
- [ ] `npm run compile` exit 0; `npm test` and `npm run lint` green; no suppression count grew; the sonar and
      text-controls censuses hold the new host file; `notification-sites.json` regenerated.
- [ ] E2S1-H1, E2S3-H1 and E2S4-H1 green in CI's real-editor job on the head, named in the pull request.
- [ ] The code gate passed, fed the scope; the own Opus review ran; every finding resolved.
- [ ] R1, R14, R15, R5, R6 and R8 recorded by the owner on a VSIX of the head before the merge.
- [ ] Docs (S5); § 12's sentence written from the code as built.
- [ ] `node .agents/conventions/tools/plan-lifecycle.mjs` clean.

## 12. The release notes, drafted in E2

E1's draft (the E1 stories, § 12) described an automatic allow that this epic removes; its Security paragraph is
replaced. **The sentence this epic adds, to be placed under the release's heading** (S5 fits it to the code as built):

> **Reading a host now needs one permission, asked once.** CoAI no longer asks a question for each host. The first
> time you open **AI accounts and usage**, or press *take the question* or *add the question* in a `Creds:` terminal,
> it asks ONE question — may it read the hosts of your `Creds: <host> / <session>` terminals, the ones open now and the
> ones you open later — and reads nothing until you choose **Allow reading**. **If you answered “Read” for a host in an
> earlier version, that answer is no longer used:** after the update the first press of a chat door asks the one
> question and reads nothing; after *Allow reading*, press again. A **Not this host** you gave stays, and so does a
> stopped round. **Withdraw the permission** in the tab or in *CoAI: hosts read over ssh* stops every read at once.
> The setting `coai.terminalAccountsAllowNewHosts` is gone.

The `POST_DEPLOY.md` question of the E1 stories (§ 12) stands; its proposed item gains *"and nothing is read before the
one permission"*. It is not edited in E2.
