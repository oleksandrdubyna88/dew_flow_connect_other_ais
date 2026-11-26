# PLAN — E2.S1 spec: one permission, its withdrawal and the one door (SECURITY SPEC)

> Status: **plan only, nothing implemented yet, 2026-11-26.** The security spec of story E2.S1 of
> [PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E2.md](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E2.md)
> (parent [PLAN_the_terminal_panel_shows_the_accounts_and_their_usage.md](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage.md),
> § 3, *Security*, *Test plan*, *Owner decisions* 21–23 and Q1). Written by Fable before any S1 code (the security row
> of `subagent-models.md`: authorisation, outside the cornerstone quota). It reworks the base that
> [the E1.S1 spec](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E1_S1_spec.md) built: the refusals file,
> its lock, its markers, the copy-in, the keeper and `regain` stay, and the permission is added to them. It keeps no
> change ledger: a change is an entry in the parent. **Reviewed at gate E2-G1 (Opus, 2026-11-26): pass after fixes.
> Its one BLOCKING change (B1) and its seven FIX changes (F1–F7) are in this text** — the stories document, § 6,
> *Gate E2-G1*, lists them, and its notes N1–N4 and N8 are folded in too; **no S1 code is written yet.** It settles
> the twelve items the stories document lists for it, with K3, K4 and K5 (§ P2). **Two of its decisions change
> sentences of the parent** (*Parent text to amend*, below): the owner took both, and the caller records them in the
> parent, which is on another branch and is not edited here. **Changed after the gate — owner decision 25,
> 2026-11-26** (recorded in the parent on another branch): on a WSL distro with systemd, a read of a Linux user opens
> a login session for that user (`/bin/login -f`, `systemd --user`, a login `bash` that runs `~/.bash_profile`, two
> empty motd files in the home), root included, and the owner accepted it on condition that the permission's words
> say so. W14 (§ P12) was rewritten for it, shorter (gate note N6), and in the same edit it reads root too, as
> the parent's decision 24 (root of a running distro is a place of its own, its `claude` and `agy` started as
> root) asks; the old *"every Linux user, but root"* predated that decision. Nothing else in this spec changed.
>
> Scope: `src_vs_code` only. `src/remoteRefusals.ts`, `src/remoteRefusalsDisk.ts` (and ONE export of
> `src/chatStoreLock.ts`: its reader of a lock as it is, § P4), `src/remoteKeeper.ts`,
> `src/remoteHosts.ts`, `src/remoteHostsSaid.ts`, `src/remoteRead.ts`, `src/remoteSessionHost.ts`, the state row in
> `src/remoteAccountsRound.ts`, NEW pure `src/remotePermission.ts`; the setting out of `package.json`,
> `src/modelKeys.ts`, `src/sideConfig.ts`, `src/configTransfer.ts`; `notification-sites.json`; the tests; one host
> scenario. Coder: Sonnet. Review: Opus. The code was read on `origin/main` at `401bbf0a` (E1 #1047 and the cut
> #1051). All paths are under `src_vs_code/` unless they start with `todo/`, `research/`, `.agents/` or are a file of
> the repository root. **Nothing was compiled or run for this spec.** Every "RED first" below is read from that code,
> not observed.

## Parent text to amend

The parent is not edited by this commit. Each row is a sentence of the parent that is no longer true when this spec
is built, with the text that replaces it. **The caller judges whether a row is a ledger entry** (`plan-change-control.md`:
in doubt, it is a change). This spec's reading is under the table.

| # | Where in the parent | It says | It must say |
|---|---|---|---|
| A1 | *Security*, residual 10 | *The permission is of one side. Withdrawn in a Windows window, it still stands in a WSL window that was given its own.* | *The permission is GIVEN for one side, and a withdrawal reaches every side that sees its copy. An "Allow" on one side allows nothing on another. A withdrawal on one side stops each other side at that side's next read of the shared state, and that side reads again only after an "Allow" pressed there. What is left: a stale write of the shared state that lowers or erases the copy — after the withdrawal and before the other side has read it, or before the withdrawal while the other side has not read since (as residual 2 for a refusal). That side then reads on until the copy is put back by a read of the withdrawing side, or until it is withdrawn there.* |
| A2 | § 3, *The sides* | *The permission is of one side, like the file: a WSL or remote window has its own, and asks its own question.* | The same sentence, and after it: *A withdrawal is not of one side: its copy in `globalState` stops every side that reads it, and each such side writes it into its own folder. A copy never gives a permission.* |
| A3 | § 3, *What a person can and cannot choose*, the list after "Not:" | — | One more item: *a withdrawal on one side of the machine only.* |
| A4 | § 3, *A withdrawal wins like a "no"* | *A write of "Allow" removes the withdrawal files but the newest, which stays.* | *A write of "Allow" removes the withdrawal files it cannot read and the ones below the newest, and leaves one readable file whose stamp is one below the grant's. An "Allow" goes above only the withdrawals that were there when it began: one that appears later ends it as not given.* |
| A5 | *Growth surfaces*, the withdrawal files | *Interrupted: a file that cannot be read counts as a withdrawal with the highest stamp.* | The same, and: *until a person's "Allow" removes it.* |
| A6 | § 3, *A withdrawal wins like a "no"* | *An "Allow" writes a grant whose stamp is one above the highest withdrawal it reads* | The same, and: *a withdrawal takes a stamp one above the highest stamp it sees — of a grant, of a withdrawal file or of the copy.* |
| A7 | *Growth surfaces*, the state row | *the copy of the Stop, the time the one notice was shown and the time the question was first asked* | The same, and: *the copy of the newest withdrawal stamp.* (The stories document, § 9, already says so.) |
| A8 | § 3, *T8, taken up* | *an answer of its own, `waiting`, beside `yes`, `no`, `ask` and `damaged`* | *beside `yes`, `no`, `no-permission` and `damaged`* (there is no `ask`: the per-host question is retired in the same section). |

**This spec's reading.** A7 and A8 are repaired wording. A5 and A6 fill a gap the parent left to this spec (*Security*:
"the specs of E2.S1 and E3.S1 repeat it for the exact store rules"): the parent says what an unreadable file counts as,
and is silent on how that ever ends (the stories document, K4). **A1 to A4 change what the parent promises**, in the
safer direction: residual 10 said a withdrawal stays on its side, while § 3 put its copy into the `globalState` that
every side reads and copies in (the stories document, K5). Both cannot hold; the caller chose the reading in which a
withdrawal stops reading wherever it can be seen. A1 to A6 have ONE cause: the withdrawal's stamp rule was folded into
the parent from the plan round's finding 1 without its edges — how far its copy reaches, and how an unreadable file
ends. If the caller counts it, it is one entry, the first of epoch 3: kinds `contract`, cause *the withdrawal's edges
are settled by the E2.S1 spec: its copy stops every side that reads it, and an "Allow" removes a withdrawal file it
cannot read*. Counted as two causes it would be the second change and a stop; this spec does not read it so, and says
so because the count is the caller's.

**Decided, 2026-11-26.** The owner took both K4 and K5, so A1–A8 stand as accepted. The entries in the parent's
ledger are the caller's, on the parent's branch.

## Judgements inside the parent's text

Named so the caller and gate E2-G1 can judge each. None is a second file format, a change of the lock's rule or a new
kind of call (the stories document, § 6, step 2).

| # | What this spec does | The parent's sentence it stands on |
|---|---|---|
| J1 | **The floor file.** After an *Allow* that wrote stamp g > 1, exactly one withdrawal file is left, `withdrawn-<g−1>.json`, and the *Allow* makes it when no readable file of that stamp is there (§ P6). It is the parent's "newest, which stays", made true also when the newest could not be read | § 3: "removes the withdrawal files but the newest, which stays" |
| J2 | **A withdrawal has no marker of its own.** The withdrawal file is made exactly as a marker is — an exclusive create, no claim — so a disk that refuses one refuses the other. What a failed withdrawal leaves is the pressing window's memory and the copy (§ P7) | § 3: "held by its window and copied to `globalState` at once"; residual 9 |
| J3 | ***Save now* also frees a lock that hangs when nothing waits** (§ P7): `saveWaiting()` — the same press, the same function, the same `take: 'person'` — claims once and releases. An *Allow* that came back `busy` is told in a notice that has that action. Without it a lock whose owner cannot be shown to be gone would keep the permission from ever being given | § 3: "a lock that is never taken over by its age … removed only by a person's press"; the E1.S1 spec, X21 |
| J4 | **One more notice site**, `accounts-permission-not-kept`: an *Allow* that the file did not take (W16). The census moves in S1's commit | § 3: "An *Allow* that the file did not take changes nothing, and the person is told" |
| J5 | **A malformed `grant` is no grant, not a damaged file** (§ P3) | § 3: "A build that does not know the key keeps it through a write" |
| J6 | **The change question offers each of its three choices only where it does something**, and no *Read* (§ P10, K3) | § 1: "a pick list with the three choices for one host"; § 3: "keeps *Not this host* and *Read this host again*" |
| J7 | **The list drops `how: 'auto'` rows at its next write, and at the cap a person's "no" pushes out the oldest "yes" row of either kind** — a "yes" row allows nothing, so losing one loses nothing (§ P11) | *Growth surfaces*: "automatic rows of the pre-release go at the next write"; E1's J6 |
| J8 | **A stamp is a whole number from 1 to 10¹²**; a larger or other value is not a stamp; at the top an *Allow* starts the count again (§ P5) | § 3: "The stamp is a whole number that only grows" |
| J9 | **NEW pure module `src/remotePermission.ts`** for the stamp rule, the withdrawal file's grammar and the question's decision: `remoteKeeper.ts` is 699 lines and `remoteRefusals.ts` 534, against the lint's 800 | The stories document, § 3: "Every decision lives in a pure module with seams" |
| J10 | **The question's words cover everything this plan's epics read** (WSL users, the usage), not only what E2's build reads. A later build that reads something the words do not name must ask again (§ P10) | *Security*, *Judgements*: "the question names the later hosts, the WSL users and the programs that are started" |
| J11 | **A withdrawal's read-back looks at the lock and does not claim it** (`lockNow`, § P4 and § P7, step 9; gate E2-G1, B1 and N5). It is a read of the disk seam, by the rule the claim already asks: nothing is made, taken over or removed, and no rule of the lock changes | § 3: "a lock that is never taken over by its age … removed only by a person's press" |

## Changes to § 4 and § 5 of the stories document (for gate E2-G1)

The gate puts these into the stories document and into the briefs of S2, S3 and S4 in one commit, before they are
coded. Nothing here was edited in the stories document by this commit but its status sentence.

| # | Where | The stories document says | This spec says | Who must follow |
|---|---|---|---|---|
| X1 | C1, the stamp rule | "An *Allow* removes every withdrawal file but the newest" | § P6: it removes the unreadable ones too, leaves the floor file, and ends as *not given* when a withdrawal appeared after it began | S3 (the sentence W16) |
| X2 | C1, the copy | "copied into the folder at every read" | § P5: the copy crosses sides on purpose (K5); the folder's highest stamp is also copied OUT when the copy is below it; an unreadable copy counts as a withdrawal until an *Allow* rewrites it | S4 (the state row), S5 (`SECURITY.md`) |
| X3 | C2 | "ONE function of the keeper" | `HostKeeper.may(who, host): Promise<DoorAnswer>`, bound as `sessions.may`. The order of its answers is § P8. `admit`, `consent`, `admitHost` and `askHost` are gone | S4 (`sessions.may('round', host)`), S3 |
| X4 | C3 | `askPermission()`, `withdraw()`, `permission()` | `sessions.askPermission(): Promise<PermissionAsked>`; `sessions.askPermissionFirst()` for the icon's first press (it asks only when the state row holds no `askedUtc` and the permission is not in force); `sessions.withdraw(): Promise<Withdrew>`; `sessions.permission(): Promise<Permission>` with the three values of C3 — a file that cannot be read is `'none'`, and each host's door says `damaged` | S3 (E2S3-T12 calls `askPermissionFirst`), S2 |
| X5 | § 5, S1, *Removed*: `Admission`'s `not-asked`, `full`, `read.how`, `allowedNow` | removed by S1 | **`Admission` is not deleted by S1.** `remoteAccountsRound.ts:57`, `:173`, `:310`, `:364-376` and `remoteAccountsRows.ts:302-329` read its kinds, and those lines are S4's and S2's. S1 leaves the type as it is, marked *the round's old view of the door, deleted by E2.S4*, and adds the pure `admissionOf(answer)` (§ P17, *The bridge*). S4 deletes the type, its mapper and the bridge line; S2 deletes `ADMISSION_SAID` | S2, S4 |
| X6 | § 10, K1: `ALLOW_NEW_HOSTS_KEY` has "no other caller" | — | It has one: `remoteAccountsHost.ts:4` and `:379` (`SETTING`, in the text of the two notices that S4 deletes). S1 replaces `SETTING` by its string literal in that one line; S4 deletes the line with the notices. Until then the key's NAME is in one source line, and E2S1-T17's scan allows that line by name | S4 (E2S4-T9 removes the allowance) |
| X7 | § 5, S1, *Removed*: W10 `ignoredWorkspace` / `ignoredFolder` | removed by S1 | `remoteAccountsRows.ts:391` (`ignoredRow`) reads both. They stay in the words table, marked *deleted with `ignoredRows`*, and the commit of S3 or S4 that deletes its last caller deletes them | S3 or S4 |
| X8 | § 5, S1, the scenario table | CUT-H1…H3 and CUT-D2…D5 are carried | `remoteHostsCut.test.ts` also holds **CUT-D1** (the session road on an automatic row, both purposes). It is carried into E2S1-T10 | The reviewer |
| X9 | § 5, S4, *Done when*: the census | "−1 for `read-remote-session` in S1, +1 for `read-accounts-permission` in S1" | S1 also adds `accounts-permission-not-kept` (J4): S1 leaves 180 sites (179 − 1 + 2) | S4 |
| X10 | E2S1-T7, "the door answers `waiting` for every host" | every host | every host that is not refused: a refused host answers `no` first (§ P8), as E1's `waitingConsent` did | The reviewer |
| X11 | C1, "past 8 files, a withdrawal first removes the oldest" | — | The lowest readable stamp goes, never the highest and never a file that cannot be read; when nothing may go, no file is made and the unreadable ones already withdraw (§ P7) | — |
| X12 | § 9, growth | "at most 8 before a withdrawal removes the oldest" | The same; and a listing reads at most 16 files — more names with the prefix count as one unreadable withdrawal (§ P4) | S5 |

**Added when gate E2-G1's changes were made in this text** (2026-11-26) — four rows the stories document does not
hold yet; the caller puts them there:

| # | Where | The stories document says | This spec says | Who must follow |
|---|---|---|---|---|
| X13 | E2S1-T17, and § 5, S1, *W10 stays* (the gate's F1) | The key's text is in `src/` only in the ONE bridge line and in the five help files | It is also in ONE line of `remoteHostsSaid.ts`: the constant `KEY` (`:21`) that W10's two sentences are built from (`:94-95`), and W10 stays until its row is deleted (X7). T17's scan allows that line by name too; the commit that deletes W10 removes the allowance (G16) | S2, or the story whose commit deletes W10 |
| X14 | C3, `sessions.withdraw(): Promise<Withdrew>` | — | `Withdrew`'s `unwritten` gains `why: 'busy'`: the withdrawal's file is made, and its read-back is not final while another window holds the claim (§ P7, step 9; B1). It is shown as every `unwritten` is: the door says `waiting`, W9 with *Save now* | S3 |
| X15 | S1's files | — | ONE export is added to `src/chatStoreLock.ts`: its reader of a lock as it is, for the disk's `lockNow` (§ P4; J11, N5). No rule of the lock changes | The reviewer |
| X16 | § 5, S1: "its § P16 the break-its R1–R30" | R1–R30 | R1–R33: R31 is B1's, R32 is F6's, R33 holds the start-again sentence of § P6, step 9 | The reviewer |

## P0 — What was read

Facts F1–F31 and V1–V11 of the E1.S1 spec stand. Added here, on `origin/main` at `401bbf0a`:

| # | Fact | Where |
|---|---|---|
| G1 | The file's parser spreads the raw object, so a key it does not know rides through every write: `refusalsOf` returns `{ ...raw, … }`, and `next` builds `{ ...file, ...parts, … }` | `src/remoteRefusals.ts:168-177`, `:239-240` |
| G2 | **A `mend` builds a new object from nothing**, so it drops every unknown key — a `grant` too | `src/remoteRefusals.ts:295-299` (`mended`) |
| G3 | `regain` merges what the window remembers into the file as it now is; it adds refusals and a Stop and carries no other key from memory | `src/remoteRefusals.ts:275-288`; `src/remoteKeeper.ts:94-95` (`regainOf`) |
| G4 | A marker is made by `writeFile(…, { flag: 'wx' })` with no claim; its listing reads each at most `UNSURE_BYTES_MAX + 1` bytes, three tries for the listing | `src/remoteRefusalsDisk.ts:105-118`, `:126-139` |
| G5 | The sweep removes only names that start with `refused.json.` and end with `.tmp` | `src/remoteRefusalsDisk.ts:78-88` |
| G6 | `take: 'person'` reaches the claim from `saveWaiting` and `stopAfterTorn` only; with nothing waiting `saveWaiting` makes no claim | `src/remoteKeeper.ts:635-662`, `:255-264` |
| G7 | The keeper has no ask seam; every operation goes through one queue; the copy of a "no" is written in the step of the press, before the queue | `src/remoteKeeper.ts:15-32`, `:155-160`, `:414-426` |
| G8 | The session door of a waiting side: `no` for a host the waiting change names, `damaged` for every other (T8) | `src/remoteKeeper.ts:397-405` |
| G9 | The road reads the consent once, in `remoteRoad`; `readOnce` checks the name only (T9); a press makes at most 3 spawns | `src/remoteRead.ts:305`, `:336-355`, `:426-438` |
| G10 | `commit` reads the stored value and calls `update` in the same step | `src/remoteHosts.ts:229-236` |
| G11 | The state row's reader checks three fields and lets other keys pass; a row that cannot be read is STOPPED | `src/remoteAccountsRound.ts:581-609` |
| G12 | No extension release holds the refusals file: the newest tag was cut before #1047. Released builds (0.87.0 to 2.0.0) know only `coai.terminalHosts` and the question `read-remote-session` | the parent, *What the code does today* |
| G13 | The test door's `restore` removes names that start with the marker prefix or end with `.lock`, and nothing else | `src/remoteSessionHost.ts:447-461` |
| G14 | `ALLOW_NEW_HOSTS_KEY` is read in `remoteAccountsHost.ts:379`; W10 in `remoteAccountsRows.ts:391`; `Admission` in the round and the rows (X5–X7) | as named |
| G15 | The disk seam can claim the lock and ask whether a claim is still its own; it has no read of a lock it does not hold. `chatStoreLock.ts` has that reader, not exported (`seen`), and the owner rule is a function over what it returns | `src/remoteRefusals.ts:353-355`; `src/chatStoreLock.ts:179`, `:378` |
| G16 | The text `coai.terminalAccountsAllowNewHosts` is in these files directly under `src/` (the ones the wiring test scans): `helpContent.ts:260`, `:621`, `:625` and the four translations `helpDe.ts`, `helpEs.ts`, `helpRu.ts`, `helpUk.ts` (`:45` or `:93`, `:269`, `:271`), which `helpCoverage.test.ts:882`, `:931`, `:945` and `:961-965` REQUIRE; `remoteHostsSaid.ts:21` (`KEY`, for W4's old row at `:64` and for W10 at `:94-95`); `configTransfer.ts:43`; a doc line each in `remoteHosts.ts:147` and `remoteKeeper.ts:24`. `modelKeys.ts:27` holds the key without its `coai.` | as named |

**UNVERIFIED.** V1, V2, V9, V10 and V11 of the E1.S1 spec are leaned on as before. New:

| # | What | What the design does about it |
|---|---|---|
| V12 | That a file made by an exclusive create and then written is seen by a reader as empty, as whole, or as not there — and for how long it can be seen empty on the person's disk | A file that cannot be read is read three times, 20 ms apart, before it counts as unreadable, and an *Allow* reads it once more before it removes it; the withdrawing window reads back and makes a new file when its own is gone (§ P6, § P7) |
| V13 | How long another window, on this side or another, takes to see a write of the state row (F28: 20 s was seen) | The copy only TELLS. On the side that took the press the withdrawal is a file, read from the disk before every call. The lag bounds residuals 10 and 15 |
| V14 | That removing a file fails for a directory or a file held open on Windows | An *Allow* that cannot remove a file it must remove writes no grant and says the storage folder must be checked (W16) |

## P1 — The threat model

What must never happen: **a host is contacted while the person's last word on the permission is "no"** — never
given, *Not now*, or withdrawn — by the round or by a chat door. What may happen, and is said: a permission that
has to be given once more.

| # | Who or what | What it can do | What holds |
|---|---|---|---|
| M1 | **A stale window** writes ANY key of `globalState`, and with it its whole old copy (F27) | Bring back an old "yes" row; drop or lower the copy of a withdrawal; drop `askedUtc`; bring back a lifted "no" row | A "yes" row allows nothing. The grant is not in `globalState`, and nothing there can give one. On this side the withdrawal is a file. A lost `askedUtc` asks once more. A "no" row that comes back refuses (residual 4) |
| M2 | **A lock holder that died** | Leave the lock. When it was an *Allow* that died after its write: leave a grant that its own read-back never checked | Its owner is gone: the next claim takes it over (E1). A note that cannot be read: *Save now* (J3). A withdrawal that was made while that *Allow* ran reads back again when the claim's owner is shown gone, and goes above the grant (§ P7, step 9) |
| M3 | **A lock holder that slept**, wakes and renames its stale `refused.json` | Put back a file with an OLD grant, or with none | A withdrawal is not in that file. When the last word was a withdrawal, its file stands at or above every grant that was written before it, in any copy of `refused.json` (§ P5, I1 and I2); a grant that an *Allow* was still writing is held by the two read-backs (§ P6, step 10; § P7, step 9). Only an *Allow* removes a withdrawal file. `regain` writes no grant (§ P3) |
| M4 | **Two sides of one machine** (a Windows window and a WSL window): two folders, ONE `globalState` | A permission of one side is read as one of the other; a withdrawal is not seen on the other | A grant is in a side's own file and is never copied. A withdrawal's copy is honoured and copied in on every side (K5) |
| M5 | **A torn write** | Half a withdrawal file; a beside-file of `refused.json` | `refused.json` is the old file or the new one (a rename). A withdrawal file that cannot be read IS a withdrawal, until an *Allow* |
| M6 | **A hostile or garbled file** in the folder, or a garbled state row | Anything in `refused.json`, in a `withdrawn-*` name or text, in the copy | Each has ONE parser with a closed shape; what does not fit narrows: a damaged file contacts nobody, a malformed grant is no grant, an unreadable withdrawal or copy is a withdrawal. None of their text is shown, logged or run. A program that can WRITE a well-made grant as the person is outside this boundary (residual 7) |
| M7 | **The upgrade** from 0.87.0…2.0.0 (a person's "yes" rows) and from the pre-release 2.0.901 (automatic rows, the refusals file, the state row, the setting) | An old answer read as a permission | The upgrade starts with no grant, whatever is stored (§ P11) |
| M8 | **A window that still runs an older build** | Read a host with an old "yes" row on a press, with no permission; the 2.0.901 round reads by itself | Not closed: until that window is reloaded (residual 8). It cannot widen what THIS build does: its writer keeps a `grant` it does not know (G1) and never touches a withdrawal file (G5, G13) |
| M9 | **A page of the tab, or a timer** | Send `allow`; tick | Neither reaches `allow()`: only the answer *Allow reading* of the modal does (§ P10). The keeper and the round have no ask seam |
| M10 | **Two presses that race** — an *Allow* and a *Withdraw* in two windows | End with the permission in force after a later withdrawal | The withdrawal wins: an *Allow* goes above only what was there when it began, and both read back — the withdrawal once more after a claim it met is over, so it does not need the *Allow*'s window to live (§ P6, § P7) |

## P2 — Decisions

**The twelve items of the stories document, each with its decision and its reason.**

| # | Item | Decision | Why | Where |
|---|---|---|---|---|
| 1 | The door's signature, its answers, each caller's action | `may(who: 'round' \| 'session', host): Promise<'yes' \| 'no' \| 'no-permission' \| 'waiting' \| 'damaged' \| 'stopped'>`. The first of these that holds: `no`, `damaged`, `waiting`, `no-permission`, `stopped` (the round only), `yes` | One function, one read of the disk, one order. The order puts the answer first whose cure the person can act on | § P8, § P9 |
| 2 | The grant's JSON; a malformed one | `"grant": { "utc": "<UTC ISO>", "stamp": <1…10¹²> }`. **Malformed is no grant**, kept through writes as it is | Both readings contact nobody. Damage has a cure that rewrites the file and stops the round; no grant has a cure of one press | § P3 |
| 3 | The withdrawal file | `withdrawn-<stamp>.json`, the stamp in decimal with no sign, no padding and no leading zero; text `{ "v": 1, "stamp": <the name's>, "utc": "<UTC ISO>" }`, at most 256 bytes. **A name with the prefix that fits nothing else is an unreadable withdrawal.** A withdrawal takes one above the highest stamp it sees. An exclusive create that meets its own name is no error: the read-back decides | One spelling for one stamp. A file that is there and cannot be understood was put there by something that wanted a withdrawal, or by a fault: both read as "withdrawn" | § P4, § P7 |
| 4 | **K4.** An unreadable withdrawal counts as the highest stamp: how does an *Allow* get above it? | **It does not go above it: it removes it.** An *Allow* is a person's press. Under the claim it removes the withdrawal files it cannot read — after three reads, and once more just before — makes the floor file, and then writes the grant. A file that appears after the *Allow* began is never removed, and ends the *Allow* as not given | The person's later word supersedes an earlier withdrawal, readable or not. The way out is the button that is always shown when there is no permission. No grant stands above a withdrawal made after it began | § P6 |
| 5 | The order of an *Allow*; a withdrawal between its steps; the pruning | Steps 0 to 11 of § P6. A withdrawal between them is a new name: the *Allow* answers *not given*; and a withdrawal whose read-back met a held claim reads back once more when that claim is over (§ P7, step 9). Pruning needs no lock: it removes only a stamp that is below another readable one | A withdrawal's file must not wait for a lock. Removing a file that is not the highest cannot change whether the permission is in force | § P6, § P7 |
| 6 | **K5.** The copy crosses sides; residual 10 says it does not | **The copy crosses sides, and it only narrows.** A withdrawal stops every side that sees its copy. A grant is never copied | The caller's rule: a withdrawal stops reading wherever it can be seen, and no copy widens. The parent's residual 10 is amended (A1–A3) | § P5 |
| 7 | `waiting` for a withdrawal: a marker, or the copy alone; *Save now*; a torn marker | **The copy and the pressing window's memory; no new marker kind** (J2). *Save now* lands it. A torn marker stays as E1 built it | A withdrawal marker would be a second file that fails exactly when the first does | § P7 |
| 8 | `regain` of a file that went back | **It writes no grant.** The grant of the file as it now is rides through, with its stamp; nothing from the window's memory raises it | A permission is given only by a press. After a stale rename the worst is one more *Allow* | § P3 |
| 9 | The words | W1–W18: nine rewritten, one widened (W9), three retired, five new, each with its test | — | § P12 |
| 10 | **K3.** The hosts list after the per-host "yes"; a `mend` "yes" | Three choices in all, each offered only where it does something: *Not this host*, *Read this host again*, *Forget*. No *Read*. A stored "yes" row shows as *stored, not used*. **A `mend` "yes" does not exist**: over damage the two answers are *Not this host* and *Read this host again*, and neither writes a "yes" | Two buttons that do the same thing teach a person that neither matters | § P10 |
| 11 | The test door | `stored()` and `restore()` also carry the withdrawal files and the state row; `allow`, `withdraw`, `may`, `permission`, `withdrawnFiles` through the keeper; raw writes only in `testDoorOf` | A scenario goes the road a press goes; only the restore is raw | § P13 |
| 12 | The residuals | 1–8 the file's, 9–12 the parent's, 13–19 this story's | — | § P14 |

**The decisions that carry them.** Decisions 1–23 of the E1.S1 spec stand, but for these: 2 and 12 — the one
function is now `may`, and `consentOf` is its refusal half; 4, 6's "automatic" half, 7's automatic half, 10, 11 and
17's "yes" rows — retired with the automatic "yes" and the setting; 13 — replaced by decision P-g. New:

- **P-a. One permission for a side, in the locked file.** Nothing in `globalState` can give it.
- **P-b. A withdrawal is a file of its own, made without the lock.** No writer of `refused.json` writes, renames or
  removes one, but the *Allow* of § P6.
- **P-c. In force means: a well-made grant whose stamp is above every withdrawal** — every file of the folder, the
  copy, and what this window holds unsaved. Anything that cannot be read on the withdrawal's side counts against
  the grant. ONE pure function says it (`inForce`).
- **P-d. A withdrawal wins a race.** An *Allow* goes above only the withdrawals that were there when it began; both
  presses read back, and a withdrawal's read-back is not final while a claim is held.
- **P-e. Only a person's press gives.** `allow()` is called from the answer *Allow reading* of the one modal, and
  from the test door. No page message, no timer, no read, no `regain`, no `mend` and no copy writes a grant.
- **P-f. Everything automatic narrows or tells.** The copy-in makes a withdrawal file; the copy-out raises the
  copy; neither removes a withdrawal or lowers a stamp.
- **P-g. Every spawn asks the door.** A press of a chat door asks the ONE question only at its first door, and only
  on `no-permission`; that press then reads nothing. A later door of a press never asks.
- **P-h. A per-host "yes" is not an answer.** No code writes one, and no code reads one as consent.
- **P-i. No state locks the person out without a sentence that names the press that ends it** (§ P14, the table).

## P3 — The record: the grant in `refused.json`

The file, its folder, its lock, `gen`, `by`, the read with three tries and the 64 KiB cap are as E1 built them. One
key is added. Version `v` stays `1`: a build that does not know the key keeps it (G1), and this build reads a file
without it as a file with no permission.

```json
{ "v": 1, "gen": 7, "by": "4242:3",
  "refused": [ { "host": "host-c", "utc": "2026-11-25T18:02:11.000Z" } ],
  "stop": { "utc": "2026-11-25T18:40:00.000Z" },
  "grant": { "utc": "2026-11-26T10:00:00.000Z", "stamp": 3 } }
```

```ts
// src/remotePermission.ts — pure
/** The highest stamp. A stamp is a whole number from 1 to this. */
export const STAMP_MAX = 1_000_000_000_000;

/** The permission of one side: when the person gave it, and its stamp. */
export interface Grant { readonly utc: string; readonly stamp: number }

/** The value as a stamp, or nothing: a safe whole number from 1 to `STAMP_MAX`. */
export function stampOf(value: unknown): number | undefined;

/** The ONE reader of the grant. Nothing else in `src/` reads the key. */
export function grantOf(file: { readonly grant?: unknown }): Grant | undefined;
```

`RefusalsFile` gains `readonly grant?: unknown` — **typed `unknown` on purpose**: the file carries the value as it
is stored, and only `grantOf` turns it into a `Grant`.

| `grant` on the disk | `grantOf` | Through a write of another change |
|---|---|---|
| absent | nothing | absent |
| an object with `utc` a string and `stamp` a stamp; other keys inside it are allowed | that grant | kept, byte for byte in value |
| anything else: not an object, an array, `utc` missing or not a string, `stamp` missing, a fraction, 0, negative, a string, above `STAMP_MAX` | **nothing — no permission. The file is NOT damaged** (J5) | kept as it is; an *Allow* replaces it |

**Why not damage.** Both readings contact nobody, so the choice is about the cure. A damaged file is mended by a
person's answer for a host, which writes the file again from the list, loses a refusal kept only in the file
(residual 6) and leaves the round stopped. That is a heavy cure for a fault in a record whose loss costs one press.
And a later build may change the record's shape: this build then reads no permission, keeps the value, and still
honours every "no" and the Stop of the same file.

**The change.** `RefusalChange` gains one kind, sent only by the store's `allow` (§ P6); the parameter type of
`RefusalsStore.commit` excludes it, so no other caller can send it:

```ts
| { readonly kind: 'grant'; readonly utc: string; readonly stamp: number; readonly floor: number }
```

| The change | The `grant` after | Answer |
|---|---|---|
| `grant(utc, stamp, floor)`, and `stamp` is not a stamp or is not above `floor` | unchanged | **`low` — a new answer of `withChange` (its type `Changed`), and NOT of `Committed`: `commit` cannot send a `grant`, and the store's `allow` turns `low` into `unwritten: lost` (§ P6, step 8). Nothing is written** |
| `grant`, and the file holds a well-made grant whose stamp is `stamp` or higher | unchanged | `kept` |
| `grant`, else | `{ utc, stamp }` | `written` |
| `refuse`, `lift`, `stop`, `start`, `migrate` | unchanged — the stored value, well made or not | as E1 |
| **`regain`** | **unchanged: the grant of the file as it NOW is. The change carries no grant, and the window's memory of a newer file gives none** (item 8) | as E1 |
| **`mend`** (over a damaged file) | **none**: the new file is built from nothing (G2), and it stays so | as E1 |

`holds` for a `grant` change (the writer's read-back): the file as it now is has a well-made grant whose stamp is
the change's or higher.

**What `regain` means for the permission.** A file that went back may hold an older grant or none. The keeper writes
the remembered refusals and the Stop into it, as E1 built, and leaves its grant alone. So after a stale rename the
permission is the stale file's: not in force when a withdrawal file stands at or above it (always, when the last
word was a withdrawal: § P5), and otherwise an older grant of the same person that was never withdrawn. A grant
that was newer is lost; the door says `no-permission`, the tab shows *Allow reading…*, and the next *Allow* writes
a grant again (it may take the lost one's stamp: two grants of one stamp are both the person's, and one withdrawal
is above both). **The keeper's memory of the newest file is never an answer and never a source of a grant.**

## P4 — The withdrawal file

```
<globalStorageUri>/terminal-hosts/refused.json            the file, with the grant
<globalStorageUri>/terminal-hosts/refused.lock            the claim (E1)
<globalStorageUri>/terminal-hosts/unsure-<token>.json     a marker (E1)
<globalStorageUri>/terminal-hosts/withdrawn-<stamp>.json  a withdrawal — NEW
```

```json
{ "v": 1, "stamp": 4, "utc": "2026-11-26T11:30:00.000Z" }
```

- **The name.** `withdrawn-` + the stamp in decimal + `.json`: `^withdrawn-([1-9][0-9]{0,12})\.json$`, and the number
  is a stamp. No sign, no padding, no leading zero: one stamp has one name, so one exclusive create is one
  withdrawal. The prefix is named ONCE, in `remoteSessionHost.ts` (`WITHDRAWN_PREFIX = 'withdrawn-'`), beside
  `REFUSED_FILE` and `MARKER_PREFIX`, and handed in as `DiskNames.withdrawnPrefix`. **The pure module does not
  spell it**: `withdrawalName` and `withdrawalOf` take the prefix as their first parameter and build the grammar
  from it, as E1's markers take `DiskNames.markerPrefix`. The store gets it from the disk it was built over
  (`RefusalsDisk.withdrawnPrefix`, below).
- **The text.** An object with `v` exactly `1`, `stamp` equal to the name's, `utc` a string; other keys are ignored;
  at most `WITHDRAWN_BYTES_MAX = 256` bytes (257 are read, so a larger file is seen as too large). That is the cap
  of the READ: this build writes under 100 bytes. A file is never written twice.
- **One parser**, pure: `withdrawalOf(prefix, name, text)`.

```ts
// src/remotePermission.ts
export type Withdrawal =
  | { readonly name: string; readonly kind: 'stamp'; readonly stamp: number; readonly utc: string }
  /** A name with the prefix that is not a whole withdrawal. It counts as a withdrawal with the highest stamp. */
  | { readonly name: string; readonly kind: 'unreadable' };

/** `prefix` is handed in (`DiskNames.withdrawnPrefix`): this module never spells it. */
export function withdrawalName(prefix: string, stamp: number): string;
export function withdrawalText(stamp: number, utc: string): string;
/** The grammar is built from `prefix`. `text` is `undefined` when the file could not be read. */
export function withdrawalOf(prefix: string, name: string, text: string | undefined): Withdrawal;
```

| A name that starts with the prefix | Reads as |
|---|---|
| fits the grammar, the text is whole, and its `stamp` is the name's | that withdrawal |
| fits the grammar; the text is empty, not JSON, not an object, another `v`, a `stamp` that is not the name's, no `utc`, larger than the cap, or the read failed | **unreadable** |
| does not fit the grammar: `withdrawn-.json`, `withdrawn-07.json`, `withdrawn-x.json`, a stamp above `STAMP_MAX`, a directory, `withdrawn-4.json.tmp` | **unreadable** |

- **The listing**, the disk's `withdrawals()`: every name of the folder with the prefix. The names that fit the
  grammar come first, the highest stamp first, then the rest in code-unit order; **the first
  `WITHDRAWN_READ_MAX = 16` are read, and the names of the others are `rest`**. A file that cannot be read is read
  again, three tries in all, 20 ms apart (V12). A name that is gone at its read was removed by another window: it
  is left out. A folder that cannot be listed, three times: `failed`.
- **A `rest` that is not empty counts as one unreadable withdrawal** (`crowded`): a folder with more withdrawal
  names than this build ever leaves (8) was filled by something else, and is not read file by file at every call.
- **Who makes one**: a person's *Withdraw* (§ P7), the copy-in (§ P5), and the floor of an *Allow* (§ P6). Always by
  an exclusive create, `'wx'`, **with no claim**, exactly as a marker is made (G4).
- **Who removes one**: an *Allow* (§ P6), and the pruning of § P7. Nothing else: not the sweep (G5: its names start
  with `refused.json.`), not a writer of `refused.json`, not `saveWaiting`, not `regain`, not a `mend`. In test mode
  only, the test door's `restore`.

```ts
// RefusalsDisk gains — the real half is in src/remoteRefusalsDisk.ts
readonly withdrawals: () => Promise<{ readonly seen: readonly { readonly name: string; readonly text: string | undefined }[]; readonly rest: readonly string[] } | 'failed'>;
/** An EXCLUSIVE CREATE of one withdrawal file, no claim. `there`: the name exists. `refused`: the disk refused. */
readonly withdrawn: (name: string, text: string) => Promise<'made' | 'there' | 'refused'>;
/** Removes one withdrawal file by its name. `false`: it could not be removed. Never throws. */
readonly unwithdraw: (name: string) => Promise<boolean>;
/** The prefix this disk was bound with (`DiskNames.withdrawnPrefix`). The store hands it to `withdrawalName` and `withdrawalOf`. */
readonly withdrawnPrefix: string;
/**
 * The lock as it is NOW, read with NO claim; it makes, takes over and removes nothing. `free`: there is no lock.
 * `gone`: the rule a claim asks (`ownerGone`) says its owner is gone — never its age. `held`: every other case,
 * a note that cannot be read among them.
 */
readonly lockNow: () => Promise<'free' | 'held' | 'gone'>;
```

**`lockNow` is a read, not a claim** (gate E2-G1: B1, N5; J11). A withdrawal's read-back asks it, and nothing else
does (§ P7, step 9). The real half reads the lock file through ONE new export of `chatStoreLock.ts`, built on the
reader that file has for its own claim (`seen`, G15) — no function and no rule that is there changes — and asks
`ownerGone(owner)` over what it read, the rule `lockHalf` already hands to `claimFile`. The lock file is not there
(the read says so): `free`. The rule says the owner is gone: `gone`. Everything else — another machine's note, a
process that answers, a note that cannot be read, a path that cannot be looked at: `held`. So `gone` is exactly
the case in which the next claim would take the lock over, and a lock that only a person's press ends reads as
`held` until that press. `seen` answers nothing both for a path that is gone and for one that will not `stat`;
the new export tells the two apart, because only the first is `free`.

`RefusalsRead`'s `file` variant gains `readonly withdrawn: { readonly files: readonly Withdrawal[]; readonly crowded: boolean }`;
a listing that `failed` makes the read `damaged`, as a marker listing does. The store's `read()` lists the
withdrawals first, then the markers, then the file. A read that runs beside an *Allow* is safe in either order:
from step 5 of § P6 on a readable file stands at or above the OLD grant, and the new grant is written only after
that.

## P5 — The stamp rule, and the copy in the state row

**The rule — ONE pure function, and nothing else decides.**

```ts
// src/remotePermission.ts
export interface Against {
  readonly files: readonly Withdrawal[];
  /** More withdrawal names are in the folder than were read. */
  readonly crowded: boolean;
  /** The copy in the state row: a stamp, nothing, or a value that is not a stamp. */
  readonly copy: number | 'unreadable' | undefined;
  /** This window holds a withdrawal that is not saved. */
  readonly held: boolean;
}

/** The permission is in force: a well-made grant whose stamp is ABOVE every withdrawal. */
export function inForce(grant: Grant | undefined, against: Against): boolean;
```

`inForce` is true only when: there is a grant; AND every file is a `stamp` below the grant's; AND not `crowded`;
AND the copy is absent or a stamp below the grant's; AND not `held`.

| Grant | Withdrawals | In force |
|---|---|---|
| none | any | no |
| 1 | none, no copy | **yes** |
| 1 | a file 1 | no — equal is not above |
| 2 | a file 1 | **yes** |
| 3 | files 1 and 2, copy 2 | **yes** |
| 3 | no file, copy 3 | no — the withdrawal is only in the copy |
| 3 | a file that cannot be read | no |
| 3 | file 2, and `crowded` | no |
| 3 | file 2, copy `'unreadable'` | no |
| 3 | file 2, `held` | no |
| malformed | none | no |

**What a stamp is taken from.**

- **A grant: one above its floor.** The floor is the highest of: every readable withdrawal's stamp, the copy when
  it is a stamp, and the stamp of the file's own grant when it has a well-made one. **The copy is in the floor, and
  another window may not have seen it yet (V13): a withdrawal made while an *Allow* runs can be below that
  *Allow*'s grant. Step 10 of § P6 and step 9 of § P7 hold it, and step 9 does not need the *Allow*'s window to
  live.** (An unreadable file gives nothing to the floor: the *Allow* removes it.)
- **A withdrawal: one above the highest stamp its window sees** — the grant in the file when it can be read, every
  readable withdrawal, the stamp in the NAME of an unreadable file whose name fits the grammar, the copy, what the
  window remembers — and never above `STAMP_MAX`.

**The invariant these two give** (the tests hold it over generated histories, E2S1-T20):

- **I1.** After an *Allow* wrote stamp g, the folder holds a readable withdrawal of stamp g − 1 (the floor file),
  or g is 1 and there is none. Only an *Allow* removes a withdrawal file that is the highest, and it leaves its
  own floor; pruning removes only a stamp that is below another. So **every grant ever written on a side is at
  most one above the highest readable withdrawal in the folder, then and at any later time**, unless a file is
  removed from outside.
- **I2.** A withdrawal is one above the highest withdrawal in the folder. By I1 it is at or above every grant that
  was WRITTEN before it, in any copy of `refused.json` — the one on the disk, one that a stale writer still has in
  its hands, and one the window could not read when the person pressed. **So a withdrawal needs no read of
  `refused.json` to win over those, and no rename of such a file can undo it.**
- **What I2 does not cover, and what does.** A grant that an *Allow* is still writing has a floor that may hold a
  copy the withdrawing window had not seen, so that grant can be above the withdrawal. From step 5 of § P6 on, the
  *Allow*'s floor file is in the folder, and a withdrawal that lists it is at or above the grant. For a withdrawal
  that was made before that, the two read-backs hold: the *Allow*'s (§ P6, step 10 — every *Allow* that wrote
  ends through it) while its window lives, and the withdrawal's own (§ P7, step 9), which is not final while a
  claim is held and goes above a grant that was written meanwhile. What neither holds is residual 5 (b).

**The top.** A stamp grows by one for each press, so `STAMP_MAX` is reached by no person. A value above it — in a
name, in the copy, in the grant — is not a stamp, and what holds it reads as unreadable. A withdrawal at the top
takes `STAMP_MAX` itself, and no grant is above that. **When an *Allow* finds its floor at `STAMP_MAX`, it starts
the count again**: it removes every withdrawal file it listed, writes the copy as absent, and writes the grant with
stamp 1 (§ P6, step 4). That state is made only by a hand or a fault, and without the restart it would end the
permission for good.

**The copy.** One field of the state row (`coai.terminalAccountsState`, the profile's `globalState`):

```json
{ "stoppedUtc": "2026-11-25T18:40:00.000Z", "withdrawnStamp": 4, "askedUtc": "2026-11-26T09:58:00.000Z" }
```

```ts
// src/remoteAccountsRound.ts — the row's reader and its merge stay in the module that owns the row
/** What the permission needs of the row. `withdrawn`: a stamp, nothing, or a value that is not a stamp. */
export function permissionRowOf(value: unknown): { readonly withdrawn: number | 'unreadable' | undefined; readonly stopped: boolean; readonly askedUtc: string | undefined };

export type StateChange =
  | /* E1's four, as they are */
  /** A withdrawal's stamp: the row's copy becomes the higher of the two. `press`: a person's press; `read`: a copy-out. */
  | { readonly withdrawn: number; readonly utc: string; readonly by: 'press' | 'read' }
  /** An *Allow*: the copy is raised to its floor; a copy that is not a stamp is REPLACED by it. `restart`: the copy is removed. */
  | { readonly floor: number; readonly utc: string; readonly restart?: true }
  /** The icon's question was asked. Written once. */
  | { readonly asked: string };
```

| The stored row | `{ withdrawn, by: 'press' }` | `{ withdrawn, by: 'read' }` | `{ floor }` |
|---|---|---|---|
| readable, the copy absent or a lower stamp | the copy is the new stamp | the same | raised to the floor when the floor is higher; else nothing |
| readable, the copy the same stamp or higher | nothing is written | nothing | nothing |
| readable, `withdrawnStamp` is not a stamp | nothing: it already counts as a withdrawal | **nothing — nothing automatic replaces an unreadable copy** | **the copy is the floor** |
| the row cannot be read at all (E1: it reads as STOPPED) | `{ withdrawnStamp, stoppedUtc: utc }` | nothing | `{ withdrawnStamp: floor, stoppedUtc: utc }` — **still stopped** |

`fieldsOk` checks `askedUtc` (absent or a string). It does NOT make the row damaged for a `withdrawnStamp` of the
wrong shape: the copy alone is then unreadable, and the row's other fields read as before (K6). The retired fields
`toldUtc` and `lastAutoNoticeUtc` are S4's.

**The keeper reaches the row through one seam**, bound by the host half over `context.globalState`, `STATE_KEY` and
`stateMerge`, so the keeper imports nothing of the round and a test hands in a fake:

```ts
// KeeperSeams gains
readonly row: {
  /** The row, read NOW from this window's `globalState`. */
  readonly read: () => { readonly withdrawn: number | 'unreadable' | undefined; readonly stopped: boolean };
  /** One commit of the row. The write is called in the step of the call (G10). It never rejects. */
  readonly change: (change: StateChange) => PromiseLike<void>;
};
```

**When the copy is written, and by whom.**

1. **At a *Withdraw* press, before anything is awaited**: a first value, one above the highest stamp this window
   already knows (the copy as it reads now, the grant and the withdrawals it remembers). After the file is made,
   the copy is raised to the file's stamp.
2. **The copy-in, at every read of a side** — every `may`, `standings`, `roundStop`, `permission` and every commit
   of a host's change: when the copy is a stamp above every readable withdrawal of the folder, and no file there is
   unreadable, a file `withdrawn-<copy>.json` is made in THIS side's folder. From then on this side keeps the
   withdrawal by itself. A copy-in that the disk does not take is a withdrawal that waits (§ P7).
3. **The copy-out, at the same reads**: when the folder's highest readable stamp is above the copy, or the copy is
   absent, the copy is raised to it (`by: 'read'`). This puts back a copy that a stale write lowered.
4. **At an *Allow***: raised to the floor; an unreadable copy is replaced; at the restart it is removed.

The copy only grows, but for the two last cases of 4, which only a person's press takes.

**K5 — the sides.** `globalState` is one for the profile; a folder is one for a side (F29). So:

- **A grant is of one side.** It is in that side's file and is never written to `globalState`. An *Allow* in a
  Windows window allows nothing in a WSL window: its door says `no-permission`, and its first chat-door press asks
  that side's own question.
- **A withdrawal is of every side that sees its copy.** The stamps of all sides are one count, because every stamp
  is taken above the copy. Side A withdraws: its stamp is one above the copy, which side B's last read raised to
  B's own highest withdrawal — and B's grant is at most one above that (I1). So A's stamp is at or above B's grant:
  B's next `may` reads the copy, answers `no-permission`, and copies it in.
- **It is given back on each side by that side's own *Allow*.** A's later *Allow* takes a stamp above the copy on
  A; on B the copied-in file still stands above B's grant.

| What happened | Side A (the press) | Side B (another side with its own grant) |
|---|---|---|
| *Allow* on A | in force | unchanged |
| *Withdraw* on A | not in force: a file | not in force at its next read: the copy, then a file of its own |
| then *Allow* on A | in force | still not in force, until an *Allow* on B |
| then a stale write lowers the copy | unchanged: files; its next read raises the copy again | unchanged: its own file |
| *Withdraw* on A, and a stale write erases the copy BEFORE B reads | not in force; its next read puts the copy back | **still in force until then — residual 10 (a)** |
| a stale write lowers the copy, B does not read, then *Withdraw* on A | not in force | **can stay in force: A's stamp was taken from too low a copy — residual 10 (b)** |
| the copy is not a stamp | not in force, until an *Allow* on A rewrites it | not in force while it is so; in force again by its own grant when the copy is a stamp below it |

The last cell: an unreadable copy that A's *Allow* rewrites lets B's own grant stand again without a press on B.
That is not a widening by a copy: B's permission was given on B by a person, the unreadable value was never a
withdrawal anyone made on B, and what A writes is A's floor, which holds every real withdrawal A has seen — a real
one that was garbled is still at or above B's grant, and B stays withdrawn.

## P6 — *Allow*, step by step

`allow()` is the keeper's; it runs in the keeper's queue and is called from two places only (P-e). The store's part
is `RefusalsStore.allow(input)`; the pure parts are `floorOf`, the `grant` merger and `inForce`.

```ts
// RefusalsStore gains
/** `copy` reads the row's copy NOW, each time it is called. */
readonly allow: (input: { readonly utc: string; readonly copy: () => number | 'unreadable' | undefined }) => Promise<Allowed>;
/** `above`: the highest stamp the pressing window knew; the withdrawal's stamp is at least one ABOVE it (§ P7, step 5). */
readonly withdraw: (input: { readonly utc: string; readonly above: number }) => Promise<Withdrawn>;
readonly copyIn: (stamp: number, utc: string) => Promise<'written' | 'kept' | 'unwritten'>;
/** A PERSON'S PRESS ONLY (`saveWaiting`): one claim with `take: 'person'`, released at once. Writes nothing. */
readonly free: () => Promise<void>;

export type Allowed =
  | { readonly kind: 'given'; readonly grant: Grant; readonly floor: number; readonly restarted: boolean }
  | { readonly kind: 'kept'; readonly grant: Grant }
  /** A withdrawal appeared after the *Allow* began, or stands above the grant at the read-back, or the read-back could not be made. `unsaved`: step 10 could not make its file. */
  | { readonly kind: 'withdrawn'; readonly unsaved?: true }
  | { readonly kind: 'damaged' }
  | { readonly kind: 'unwritten'; readonly why: 'busy' | 'disk' | 'lost' };
```

The steps. A step that ends the *Allow* releases the claim first.

0. **Before the claim: list the withdrawal files and read the copy.** These names (`seen` and `rest`) and this
   copy are *what was there when the Allow began*. A withdrawal that this window still holds unsaved is part of
   it: the keeper keeps holding it while the *Allow* runs, and drops it only when the *Allow* is `given` or `kept`
   — the *Allow* is then the person's later word, as a *Read* drops a held refusal of its host in E1. **Step 3
   also counts as new a readable withdrawal whose `utc` is later than the *Allow*'s `utc` (taken at the press),
   whatever listing it was in**: the queue may reach this step long after the press, and a *Withdraw* that was
   pressed in another window in between is the later word. (A copy-in that was made in that gap counts too: it
   narrows, and costs one more press.)
1. **Claim** — the ordinary claim, `doing: 'grant'`, **never `take: 'person'`**. `held` after `CLAIM_ROUNDS`:
   `unwritten: busy`. `failed`: `unwritten: disk`.
2. **Read `refused.json`** under the claim. It cannot be read: `damaged` — a damaged file is mended first (§ P10).
3. **List the withdrawal files again, and read the copy again.** `failed`: `damaged`. Then, in this order:
   **the file's grant is already in force** against this listing and the copy: `kept` — another window allowed
   meanwhile, or the permission was never gone. **Else a name that was not in the listing of step 0, or a readable
   withdrawal with a later `utc` than the *Allow*'s (step 0), or a copy that is now a higher stamp than at step 0:
   `withdrawn`.** The floor file that this *Allow* made itself at step 5, in a round before a start again, is not
   a new name (as in step 10). In both cases nothing is written and nothing is removed.
4. **The floor and the stamp.** `floorOf` over the readable files, the copy when it is a stamp and the file's own
   grant; the stamp is the floor plus one. **The floor is `STAMP_MAX`**: the restart — the floor is 0, the stamp
   1, every listed name goes to step 6's removals, and step 5 is skipped.
5. **The floor file.** When the floor is 1 or more and step 3's listing has no readable `withdrawn-<floor>.json`:
   make it by an exclusive create, with the *Allow*'s own `utc` (so step 3 never reads it as a later word), and
   read it back. **Its name is taken by a file that cannot be read: the floor is one higher**, and this step runs
   again (three times at most). Not made, or not readable: `unwritten: disk`.
   From here on the old grant is not in force whatever happens: a readable file stands at or above it.
6. **Remove what cannot be read**: every name of step 3's listing that is unreadable, each read once more just
   before. A file that reads now is not removed, and the *Allow* goes back to step 3 — three times at most, then
   `unwritten: lost`. `crowded`: every name of `rest` goes too, at most `ALLOW_REMOVALS_MAX = 1024` names in one
   press; when more are left the answer is `unwritten: lost`, and the next press takes the next 1024. **One that
   cannot be removed: `unwritten: disk`, and no grant is written** — it could not be in force.
7. **The claim, once more** (E1's step 5): fresh by this window's clock and still ours; else release and start
   again at 1. The beside-files are swept first, as E1.
8. **Replace**: the merged file with `grant: { utc, stamp }`. The merger refuses a stamp that is not above the
   floor (`low` → `unwritten: lost`: it cannot happen here, and the merger is the guard that says so). A throw:
   `unwritten: disk`.
9. **Release**, and note whether the lock was still this claim's; if it was not, start again at 1 (E1's step 7).
   **An *Allow* that has written its grant never answers `kept` at step 3 of a later round**: when it finds its
   own grant in force there, it releases and goes to step 10, and "step 3's listing" in step 10 stays the one of
   the round that wrote. Else a start again would end as `kept` over a withdrawal that was made below the grant
   while the *Allow* ran (§ P5), and step 10 would never look. (This sentence is not in gate E2-G1's text; it is
   what makes "step 10 holds it" true on this path.)
10. **Read back, with no claim**: the withdrawal listing, then the file. The grant in the file is this stamp or a
    higher one, every file of the listing is a stamp below it, AND the listing holds no name that step 3 did not
    see (but the floor of step 5): `given`. The grant is not there: another writer renamed since — again from 1,
    `WRITE_ROUNDS` times, then `unwritten: lost`. **A withdrawal file stands at or above the grant, or cannot be
    read, or is a NEW name whatever its stamp: a withdrawal came while the *Allow* ran, and it wins.** When no
    withdrawal file stands at or above the grant yet, the *Allow* makes one itself, `withdrawn-<the grant's
    stamp>.json`, and reads it back; then `withdrawn`. When that file cannot be made the answer is `withdrawn`
    with `unsaved: true`, and the keeper holds a withdrawal as if the person had pressed it in this window (§ P7).
    **A listing or a read that fails: as a new name — the *Allow* makes `withdrawn-<the grant's stamp>.json`, and
    answers `withdrawn` (`unsaved` when that fails).** A read-back that cannot be made never ends as `given`.
    (The copy is the keeper's to judge, below: the store may have met a copy that is not a stamp, which only the
    keeper's row change replaces.)
11. **Remove the lower files**: every readable withdrawal of step 3's listing whose stamp is below the floor.
    Never the floor, and never a name that was not in that listing. A removal that fails here is not a failure of
    the *Allow*: a lower file changes nothing, and the next *Allow* or the pruning takes it.

Then the keeper, on `given` or `kept`: it first drops a withdrawal it held unsaved (the person's later word, in
the order of this window's queue); on `given`, the row's `{ floor, utc }` (with `restart` when the store says so);
then what it remembers of the file, and the change signal. On any other answer it drops nothing. **It answers
`given` — and passes a `kept` on — only when its own `inForce`, with `held` false after that drop, over a fresh
read of the side and of the row, says yes; else `withdrawn`** — a copy that another side raised meanwhile is a
withdrawal like any other. Without the drop on `kept`, the person would be told *given* (§ P10) while the next
read lands the held withdrawal above the grant.

**Why this order.** The floor is made before anything is removed, so that removing an unreadable file can never
leave the OLD grant in force for a moment — and then report "not saved" when the write fails. The unreadable files
go before the grant is written, so a grant is never written that could not be in force. The lower files go last,
after the read-back, because they are only growth. The grant is written under the claim; nothing else here needs
the claim, and a withdrawal's file never waits for it (the last word of its read-back does: § P7, step 9).

**A withdrawal between the steps.**

| When the withdrawal's file appears | What the *Allow* does |
|---|---|
| before step 0 | It is part of what was there: the person pressed *Allow* after it. The grant goes above it |
| between step 0 and step 3 — also while the *Allow* waits for a held claim | step 3 sees a new name: `withdrawn`, nothing written |
| between step 3 and step 10 | Step 10 sees a name that step 3 did not: `withdrawn`. Its stamp is one above everything its window saw. Where that window had seen the copy, it is the grant's own stamp or higher — equal is not above. Where it saw less (a copy that had not reached it yet, V13), it can be below the grant, and the *Allow* makes the withdrawal at its own stamp |
| the same, and the *Allow*'s window closes, reloads or crashes after step 8 and before step 10 | Step 10 never runs. The withdrawal's own read-back met a held claim, so it was not final (§ P7, step 9): when the claim's owner is shown gone it reads back again, finds the grant at or above its stamp, and makes a higher withdrawal. It does not need the *Allow*'s window |
| the same, and step 10's listing or read fails | As a new name: the *Allow* makes the withdrawal at its own stamp, and answers `withdrawn` |
| after step 10's listing | Its window read back after the grant was written (its file was made after that listing, and it reads back after it makes): it sees the grant in force, and makes a higher withdrawal (§ P7, step 9) |
| it is the SAME window | The keeper's queue runs them in the order of the presses: a *Withdraw* pressed second is written second, above the grant |

In every row the person who pressed *Withdraw* last ends withdrawn — in the row of the *Allow* that died, while
the withdrawing window lives until its read-back is final (residual 9 names what is left). The person who pressed
*Allow* is told when it did not hold (W16), and may press again.

**What an *Allow* that was not `given` leaves.** For `unwritten`, `damaged` and `withdrawn`: no grant of this press
in force. A floor file may have been made and unreadable files removed; both only narrow or change nothing, since
the old grant is at or below the floor. After step 10's `withdrawn` the grant of this press is in the file with a
withdrawal at or above it. **The keeper holds nothing and marks nothing for an *Allow***: a widening is never tried
again by itself. (What it holds after `unsaved` is a withdrawal, which narrows.) The person is told (W16) and
presses again.

## P7 — *Withdraw*, step by step; what waits; *Save now*

`withdraw()` is the keeper's, called from a person's press: the tab's `withdraw` message (S3), the first row of
the hosts list (§ P10), the test door. It narrows, so it asks nothing.

**In the step of the press, before anything is awaited:**

1. This window holds a withdrawal: `pending = { utc }`. From now its door answers `waiting`, then `no-permission`.
2. The row's `{ withdrawn: first, utc, by: 'press' }`, where `first` is one above the highest stamp this window
   knows now. The write is called in this step (G10).
3. The change signal, so the round abandons what it is doing (S4).

**Then, in the keeper's queue — the store's `withdraw({ utc, above })`, with NO claim** (`above`: the highest
stamp this window knew at the press, so step 2's `first` is one above it):

4. **List** the withdrawal files, and read `refused.json`. A file that cannot be read is no obstacle (I2). A folder
   that cannot be listed: `unwritten: disk`.
5. **The stamp**: one above the highest of the grant, the listing's stamps (the names of unreadable files that fit
   the grammar among them) and `above` — at most `STAMP_MAX`.
6. **Prune**, when 8 or more names with the prefix are there: the readable file with the LOWEST stamp goes, and
   the next, until fewer than 8 are left or no file may go. A file may go only when another readable file has a
   higher stamp. A file that cannot be read never goes here.
7. **Still 8 or more**: none is made. This is so only when files that cannot be read fill the folder, and each of
   them is a withdrawal already.
8. **Else an exclusive create** of `withdrawn-<stamp>.json`. `there` is no error: another window made the same
   withdrawal. `refused`: `unwritten: disk`.
9. **Read back**: the lock (`lockNow`, § P4), then the listing, then the file. **Not in force over the folder
   alone** — a readable withdrawal at or above the file's grant, or an unreadable one, or no grant: `withdrawn`,
   with the stamp. The file cannot be read and the withdrawal of step 8 is in the listing: `withdrawn` too (I2).
   **In force**: an *Allow* wrote in between — again from 4, `WITHDRAW_ROUNDS = 3` times, then `unwritten: lost`.
   **A claim is held (its note read without a claim): the read-back is not final. It reads back again when the
   claim is released or its owner is shown gone (E1's test, never by age), at most `WITHDRAW_ROUNDS` times; a
   grant at or above its stamp then → again from 4. Until it is final, `pending` stays.**

**How step 9 looks at the lock** (gate E2-G1, B1).

- **The lock is looked at FIRST**, before the listing and the file. A claim that ended before the look has its
  write in the file that is read after it. A claim that begins after the look, when it is an *Allow*'s, lists at
  its step 3 after it claimed, and the withdrawal's file was made at step 8, before the look: that *Allow* meets
  it as a new name or as a later `utc` (§ P6, steps 0 and 3) — or it was pressed after the withdrawal and is the
  later word. A look after the reads would miss a grant that was written and released between them.
- **`free` or `gone`**: the read-back that follows stands. A lock whose owner is gone is a claim that is over: a
  process that is gone renames nothing.
- **`held`**: the store looks again as a claim waits — `CLAIM_ROUNDS` looks, `CLAIM_PAUSE_MS` apart — and reads
  back when the lock is `free` or `gone`. **Still `held` after that: `unwritten: busy`.** The file of step 8
  stands, and every window of the side reads it; only the word *withdrawn* is not said, and `pending` stays. The
  lock is never taken over here, and never judged by its age.
- The "at most `WITHDRAW_ROUNDS` times" is the count of *again from 4*, which this shares. The wait is bounded:
  1 s for one read-back, 3 s for a press, in the keeper's queue — as a change that waits for the claim in E1.

```ts
export type Withdrawn =
  | { readonly kind: 'withdrawn'; readonly stamp: number }
  /** `busy`: the file is made, and a claim was still held at the last read-back. `disk`: no file could be made. `lost`: an *Allow* kept writing above it. */
  | { readonly kind: 'unwritten'; readonly why: 'busy' | 'disk' | 'lost' };
```

**Then the keeper**: on `withdrawn`, the row is raised to the stamp, `pending` is dropped, the change signal
fires. On `unwritten`, `pending` stays and the person is told once (W9's third title, with *Save now*). After
`busy` the file that was made is in the folder: the copy-out of the next read raises the copy to it (§ P5).

**A withdrawal that waits.** Something of a withdrawal waits on a side when (a) this window's `pending` is set —
no file could be made, or its read-back is not final — or (b) the copy is a stamp above every readable withdrawal
of the folder and the copy-in just failed. While it waits:

- **The door answers `waiting`** for every host that is not refused — both callers, in this window by (a) and in
  every window of the side that has seen the copy by (b). No host is contacted.
- **Every read tries to land it**, with no press: (a) by the store's `withdraw` again, with a stamp taken THEN;
  (b) by the copy-in again. Neither needs the lock to make its file, so a hung lock cannot keep a withdrawal's
  file out. What a hung lock does keep back is the last word of (a)'s read-back (`busy`): the pressing window says
  `waiting` until the lock is free, its owner is gone, or *Save now* takes it.
- ***Save now* lands it too** (`saveWaiting()`), and tells again when it still fails.
- **There is no marker for it** (item 7, J2). A marker is an exclusive create in the same folder: where the
  withdrawal file cannot be made, a marker cannot be made either. So what holds a withdrawal that the disk did
  not take is this window's memory and the copy — the parent's residual 9, as it says.
- **A torn marker** is E1's: it waits, only *Save now* removes it, and the round is stopped after it. This build
  makes no marker for a withdrawal, so a torn marker is not one of its withdrawals; `saveWaiting` does not withdraw
  the permission for it.

**`saveWaiting()`, as built and with two additions.** It stays a person's press only, and stays the ONE caller that
passes `take: 'person'`. It lands every change that waits (E1); **when that made no claim at all, it calls the
store's `free()` once** — one claim with `take: 'person'`, released at once, nothing written — so a person can
free a lock whose owner cannot be shown to be gone, before an *Allow*, a *Read this host again* or a start (J3);
**and LAST it lands a withdrawal that waits.** In this order, because the withdrawal's read-back is not final over
a held lock (step 9): it is made after the press took the lock. `held()` counts a withdrawal that waits as one.

## P8 — The one door

```ts
// src/remotePermission.ts
export type Asker = 'round' | 'session';
/** The one door's answers. `stopped` is given to the round only. A host is contacted on `yes` and on nothing else. */
export type DoorAnswer = 'yes' | 'no' | 'no-permission' | 'waiting' | 'damaged' | 'stopped';
export type SessionAnswer = Exclude<DoorAnswer, 'stopped'>;

// HostKeeper: `admit` and `consent` are gone. It gains
readonly may: (who: Asker, host: string) => Promise<DoorAnswer>;
readonly allow: () => Promise<Allowed>;
readonly withdraw: () => Promise<Withdrew>;      // 'withdrawn' | { kind: 'unwritten'; why: 'busy' | 'disk' | 'lost' }
readonly permission: () => Promise<Permission>;  // { given: string } | 'none' | 'waiting'
```

**`may` reads everything afresh, in the keeper's queue**: the side — the withdrawal listing, the markers, the
file — with E1's `sync()` (a file that went back is written forward; what waits is tried; the "no" rows are copied
in), then the copy-in and the copy-out of § P5, then the list and the row. It answers from no value it kept. **The
first row of this table that holds is the answer:**

| # | Answer | When | Who gets it |
|---|---|---|---|
| 1 | `no` | The host is refused: in the file; as a readable "no" row of the list; as a refusal this window holds unsaved; or in a marker that waits | both |
| 2 | `damaged` | The file or its folder cannot be read (the withdrawal listing and the marker listing among them), or the list cannot be read | both |
| 3 | `waiting` | Something is not saved on this side: a refusal, a Stop or a withdrawal this window holds; a marker that waits, a torn one among them; a copy-in that the disk did not take | both |
| 4 | `no-permission` | `inForce` says no | both |
| 5 | `stopped` | `who` is `'round'`, and the Stop is in the file, held by this window, in a marker, or in the row's copy | the round |
| 6 | `yes` | none of the above | both |

**Why this order.** `no` first: it is the one answer about THIS host, it is the person's own, and it stays true
whatever else is wrong. `damaged` before `waiting`: *Save now* cannot write over a file that cannot be read, so the
mend comes first. `waiting` before `no-permission`: while a withdrawal waits, the question "allow?" would be asked
over a state that is not on the disk. `stopped` last: the Stop is the round's own, and a stopped round with no
permission must say the permission first.

**The cells of C2's table.** `yes` needs every condition of its row; each condition that fails has its answer:

| Condition of C2 | It fails as | Round | Session |
|---|---|---|---|
| the folder can be read | `damaged` | no call | no spawn |
| nothing waits unsaved | `waiting` | no call | no spawn |
| the permission is in force | `no-permission` | no call | no spawn |
| the scope host is not refused | `no` | no call | no spawn |
| the round is not stopped | `stopped` | no call | — not asked: the Stop does not stop a session read |

**What each caller does with each answer.**

| Answer | The round (S4 builds it; this is the contract) | The session road, the FIRST door of a press | The session road, a LATER door of the press |
|---|---|---|---|
| `yes` | the call | the spawn | the spawn |
| `no` | skips the host; its reading is dropped | *turned off for this host* (the road's sentence of today), no spawn | the same sentence; the press ends |
| `no-permission` | ends the round; no timer; the readings are dropped | **asks the ONE question; whatever is answered, this press spawns nothing** (W15) | W15's third sentence; the press ends; **never asks** |
| `waiting` | skips every host; the tab says W12 | W12, no spawn, no question | W12; the press ends |
| `damaged` | skips every host; the tab says W11 | W6, no spawn, no question | W6; the press ends |
| `stopped` | ends the round | never given | never given |

The round asks before EVERY call and once more before a result is published, as E1's round asked `admit`. Neither
caller, and nothing in the keeper, asks a person from the door: the keeper's seams have no ask, as in E1.

**`consentOf` is the door's refusal half** (`remoteHosts.ts`): `consentOf(file, value, host): 'no' | 'damaged' | 'open'`.
It loses `allowNew` and `automatic`; a "yes" row and an automatic row are `open`, like no row. `refusedBy` and
`readsKey` keep their meaning: only a "no" hides the terminal's two items.

**`permission()`**, for the tab and the hosts list, from the same fresh read: `'waiting'` when row 3 holds;
`{ given: <the grant's utc> }` when `inForce`; else `'none'` — a file that cannot be read is `'none'` here, and
every host's door says `damaged`.

## P9 — The session road (T8, T9)

```ts
// RoadSeams: `store.consent`, `store.answer` and `ask` are gone. It gains
readonly store: {
  /** The one door, asked before EVERY spawn of a press. */
  readonly may: (host: string) => PromiseLike<SessionAnswer>;
};
/** The ONE question. Called only at the first door of a press, and only on `no-permission`. */
readonly askPermission: () => PromiseLike<PermissionAsked>;
```

- **The door is in `readOnce`, and nowhere else in the road.** `remoteRoad` no longer asks before the first leg:
  `readOnce` asks before every spawn, and the first spawn of a press is the one for which `trip.calls` is 0. So a
  press has one code path for its first spawn and its later ones, and a new fallback cannot forget the door.
- **The order in `readOnce`**: the door (awaited); then the name check `stillThere` (not awaited); then the spawn.
  The door is the last `await` before the spawn, and the name check is the last thing before it — the door can
  take time, and the terminal may have been renamed meanwhile.
- **The first door, `no-permission`**: `askPermission()`. `given` → W15's second sentence, *press again*; `not-now`
  → W15's first; `not-kept` → W16 as the store answered. **In no case does this press spawn**: the person allowed
  the reading, not this press, and the next press asks the door again.
- **A later door** (the titles fallback, the tail fallback, the call after a pick — a pick can be open for
  minutes): any answer but `yes` ends the press with that answer's sentence. It never calls `askPermission`
  (E2S1-T13 hands in one that throws).
- **T8**: `waiting` has its own sentence, W12; W6 is said for `damaged` only.
- **What does not change**: the name re-check, `CALLS_MAX = 3`, the latch of one read for a terminal, the picked
  id, the progress line, every other sentence of the road. `notKept`, `asked` and `sayKept` go with the per-host
  question.

## P10 — The question, and the hosts list (K3)

**The question is one pure decision over seams**, so a unit test holds who is asked and when:

```ts
// src/remotePermission.ts
export type Permission = { readonly given: string } | 'none' | 'waiting';
export type PermissionAsked = 'given' | 'not-now' | { readonly kind: 'not-kept'; readonly said: string };

export interface AskSeams {
  /** The modal of W14. `true`: *Allow reading*. `false`: *Not now*, or Escape. */
  readonly raise: () => PromiseLike<boolean>;
  readonly permission: () => PromiseLike<Permission>;
  /** The keeper's `allow()`. */
  readonly allow: () => PromiseLike<Allowed>;
  /** The row's `askedUtc` as it reads now, and its one write. */
  readonly askedBefore: () => boolean;
  readonly noteAsked: () => PromiseLike<void>;
  /** W16 for a result that is not `given` or `kept`; for `unwritten` it also raises the notice with *Save now*. */
  readonly notKept: (result: Allowed) => string;
}

export function permissionAsker(seams: AskSeams): {
  /** Any press that asks: the tab's *Allow reading…*, the hosts list's first row, a chat door. */
  readonly ask: () => Promise<PermissionAsked>;
  /** The icon's press: asks only the FIRST time. */
  readonly askFirst: () => Promise<PermissionAsked | 'asked-before'>;
};
```

| | `ask()` | `askFirst()` |
|---|---|---|
| the permission is in force | `given`, no modal | `given`, no modal |
| the row holds `askedUtc` | — it does not look | `asked-before`, no modal |
| else | the modal | `noteAsked()` FIRST, then the modal — so a second press while the modal is open, or after it, does not raise a second one. A row that is lost asks once more |
| *Not now* or Escape | `not-now`; **nothing is written** | the same |
| *Allow reading* | `allow()`: `given` or `kept` → `given`; anything else → `not-kept` with W16 | the same |

One modal at a time in a window: a second `ask` while one is open gets the first one's answer. **Who calls them**:
`askFirst` — the command of the icon (S3); `ask` — the tab's message `allow` (S3), the first row of the hosts list,
and the road's `askPermission` seam. Nothing else. The round's seams, the timer and the keeper have neither.

**The modal**, in `remoteSessionHost.ts`, the one place that raises it: `notifyAndAsk` with `as: 'warning'`,
`class: 'confirmation'`, `source: 'remoteSessions'`, **`code: 'read-accounts-permission'`**, `modal: true`, W14's
title and detail, and the two actions *Allow reading* and *Not now*. The question's per-host forerunner
(`askQuestion`, `code: 'read-remote-session'`, the button *Read*) is deleted with `askHostOf`.

**J10 — what the words cover.** W14 names everything this plan's epics read under the permission: the later
hosts, the WSL users with the login session a read of each opens on a systemd distro (E3; owner decision 25), the usage and the programs that are started for it (E4), the session of a chat door.
E2's own build reads less than that — no WSL, no Claude usage — and the words say what is ALLOWED, so they are
true in every build of this plan. **A build that reads something W14 does not name must not use this grant.** It
then adds a key to the record that says what was asked, and reads a grant without that key as this question's; no
such key is needed today, and none is written.

**The hosts list — *CoAI: hosts read over ssh*.** A pick list, as today.

- **Its first row is the permission** (W18), one of four: no permission — *Allow reading…* (→ `ask()`); in force —
  *Withdraw the permission*, with the time it was given (→ `withdraw()` at once, then W17); something waits —
  *Save now* (→ `saveWaiting()`); the list or the file cannot be read — a row that says so and how it is mended.
  So the list is never empty, and W5's two "empty" sentences are retired.
- **Then the hosts**, in code-unit order: every host that is refused, every host of a live `Creds:` terminal, and
  every host with a stored row. (Before, the live hosts were listed only over a damaged list; now a person can
  refuse a host here before anything is read.) Each row says how the host stands (W4).
- **A stored "yes" row** — a person's of 0.87.0 or an automatic one of the pre-release — shows as what it is: the
  host stands like any host that is not refused, and its detail says *an older “Read” answer is stored and not
  used* (W4b). It is never an answer.

**The change question for one host** (code `change-remote-host-answer`, as today; W1 and W3). **Three choices in
all; each is offered only where it does something; there is no *Read*.**

| The host | The buttons | What the press sends |
|---|---|---|
| is refused — in the file, as a row, held or in a marker | *Read this host again* | `{ kind: 'forget', host }`: the refusal is lifted in the file first, then its row goes (E1's road of a lift). **No "yes" row is written** |
| is not refused, and a "yes" row is stored for it | *Not this host* · *Forget* | *Not this host*: `{ kind: 'person', host, answer: 'no' }`, the copy first, then the file (E1). *Forget*: `{ kind: 'forget', host }` — the stored row goes; what is read does not change |
| is not refused, and nothing is stored | *Not this host* | the same |
| the list or the file cannot be read | *Not this host* · *Read this host again* | `{ kind: 'mend', host, answer: 'no' }` · `{ kind: 'mend', host, answer: 'forget' }` |

```ts
// src/remoteHosts.ts
export type HostChange =
  | { readonly kind: 'person'; readonly host: string; readonly answer: 'no' }
  | { readonly kind: 'forget'; readonly host: string }
  | { readonly kind: 'mend'; readonly host: string; readonly answer: 'no' | 'forget' };
```

- **Why each only where it does something.** With the per-host "yes" gone a host has two states. *Read this host
  again* and *Forget* would do the same thing for a refused host, and *Read this host again* nothing for another;
  two buttons with one effect teach a person that neither matters. *Read this host again* is the one of the three
  that widens, and it is given only by this modal — the tab's `read-again` raises it too (S3).
- **A `mend` "yes" does not exist.** Over a damaged list or file, *Read this host again* is `mend` with `forget`:
  the readable rows are kept, this host ends with no row and no refusal, and nothing is read by that press. **A
  mended FILE has no grant and leaves the round stopped**; W7b says both before the person answers. The buttons
  over damage are the two that can each be the person's answer for a host whose standing cannot be read.
- **A change that the store did not keep** is told as today (W6).

## P11 — The upgrade, and what is retired

**The upgrade starts with no permission, whatever is stored.** No migration runs; nothing is rewritten at
activation.

| Stored before | Read by this build as | The test |
|---|---|---|
| A person's "yes" row (0.87.0 … 2.0.0) | A row that is kept and is no answer. The first press of a chat door on that host asks the ONE question and reads nothing | T10, T11 |
| An automatic row, `how: 'auto'` (the pre-release 2.0.901) | No answer. **It is dropped by the next write of the list** (J7); a list that holds one is not damaged | T11, T23 |
| A "no" row | A refusal: the copy of one, copied into this side's file at the first read (E1) | T11 |
| `refused.json` of the pre-release: `v`, `gen`, `by`, `refused`, `stop`, no `grant` | That file, with no permission | T1, T11 |
| The Stop, in the file or as `stoppedUtc` | Stopped. An *Allow* does not touch it: the round stays stopped until *Start* | T11 |
| `unsure-*` markers of the pre-release | As E1: they wait, and the door says `waiting` | T12 |
| The state row with `toldUtc`, `lastAutoNoticeUtc` | Read, not damaged; S4 stops writing them | E2S4-T9 |
| `coai.terminalAccountsAllowNewHosts` in a settings file | Not read: no source reads the key, and it is not in the manifest. An exported file that holds it is refused on import as *unknown to this build* | T17 |
| Nothing at all (a new profile) | No file, no row: `no-permission` | T2, H1 |

**What is retired** is the stories document's list (§ 5, E2.S1, *Removed*), with three exceptions that wait for the
story whose code still reads them (X5, X6, X7): the type `Admission`, the one line that names the setting's key,
and W10. In `remoteHosts.ts` after it:

- The parser still reads a "yes" row and `how: 'auto'` (a list that holds one is not damaged). A `how` it does not
  know, and an `'auto'` beside a "no", are damage, as today.
- **No function builds a "yes" row.** `personal` writes a "no"; `automatic`, the `auto` arm of `APPLY`, `yesOf`,
  `rowConsent`, `AutomaticAllow`, `AUTOMATIC_ALLOW` and `autoCounts` are deleted.
- **Every write of the list leaves out the `how: 'auto'` rows.** At the cap of 100 a person's "no" pushes out the
  oldest "yes" row; **never a "no"**; 100 refusals are `full`, as today (J7).
- `standingsOf` keeps `how`, so the hosts list can say which kind of old row is stored.

**The setting** `coai.terminalAccountsAllowNewHosts` leaves `package.json`, `SAFETY_KEYS` (which is then
`['kimiAllowNetworkSignIn']`), `allowNewHostsFrom`, `ignoredLayerOf`, `allowsNewHosts`, `ignoredLayerFor`,
`NEVER_TRANSFERRED`, the `SETTING_ALIAS` of the help census, and `terminalAccountsSetting.test.ts`. The constant
`ALLOW_NEW_HOSTS_KEY` goes too; the one line of `remoteAccountsHost.ts` that used it holds the key's text until S4
deletes that line (X6). **The key's TEXT stays in two more places, and no code reads it there** (gate E2-G1, F1;
G16): the five help files, whose tests require it until S5 rewrites them (E2S5-T2), and the one constant `KEY` of
`remoteHostsSaid.ts`, from which W10's two sentences are built until W10 is deleted (X7, X13). W4's old row, the
other user of that constant, is rewritten by S1. The doc lines of `remoteHosts.ts` and `remoteKeeper.ts` that name
the key go with the code they describe.

**A window of an older build beside this one** (M8) is not closed by anything here. A released build reads a
person's "yes" row and logs in on a press, with no permission and after a withdrawal; it does not read the file,
the withdrawal files or the copy. The pre-release build runs its own round. Both end when the window is reloaded.
This build writes no "yes" row, so no NEW host becomes readable there (residual 8).

## P12 — The words, in English

All in `src/remoteHostsSaid.ts`, in the one `HOSTS_SAID` table, so a unit test holds each (E2S1-T14, T15, and the
table test of `remoteHostsWiring.test.ts`). The codes stay string literals at their call sites. No path and no text
of a disk error is in any of them. `<why>` is one of E1's four fixed phrases. `<side>` is `thisSideLabel(…)`.
Buttons: *Allow reading*, *Not now*, *Not this host*, *Read this host again*, *Forget*, *Save now*.

| # | State | Where | The words |
|---|---|---|---|
| W1 | rewritten | The change question: the title | *Read the host “<host>” over ssh, or not?* |
| W2 | **retired** | The per-host question's detail | — |
| W3 | rewritten | The change question: the detail — how the host stands, then one sentence for each button that is offered, then the side | Standing, one of: *Your answer now: Not this host, given <askedUtc>.* · *This host is read under your permission.* · *This host is not read: there is no permission to read hosts.* Then, when a "yes" row is stored: *An answer “Read” of an earlier version is stored (given <askedUtc>); it is not used any more.* Buttons: *“Not this host” stops every ssh call of CoAI to this host, from the accounts round and from the chat doors.* · *“Read this host again” removes your “Not this host”: the host is then read under the permission like any other.* · *“Forget” removes the stored older answer. It changes nothing about what is read.* The side: *A “Not this host” is kept on the side of this machine where you gave it — <side>. “Read this host again” in any window of that side lifts it. If the host is still not read a moment later, another window had not caught up yet: choose it once more. On another side (Windows, a WSL distro, a remote machine) choose it there.* |
| W4 | rewritten | The hosts list: a host's description | refused: *not read: “Not this host”* · not refused, the permission in force: *read under the permission* · not refused, no permission: *not read: no permission* |
| W4b | rewritten | The same row: its detail | refused: *given <askedUtc>* · a person's "yes" row stored: *an older “Read” answer is stored and not used* · an automatic row stored: *an automatic entry of a test build is stored and not used* · nothing stored: no detail |
| W4c | kept | A host for which no answer can be read | *no answer can be read for this host* |
| W5 | **retired** | The notice of an empty list (three sentences) | — the list always has its first row; W18 says the damage |
| W6 | rewritten (the first) | The session door on `damaged`; an answer that was not kept | *The list of hosts read over ssh cannot be read, so nothing was read. Run CoAI: hosts read over ssh and choose “Not this host” or “Read this host again” for a host there: your answer repairs the list.* · the two others as built (100 refusals; *could not be saved … Answer again in a moment*) |
| W7 | rewritten (its end) | The change question over a damaged list, before W3 | As built up to *…and the entries that cannot are dropped.* Then: *A host of a dropped entry has no answer afterwards and is read under the permission like any other host. Look through the list afterwards.* |
| W7b | rewritten (its end) | The change question over a damaged file, before W3 | As built up to *…and so may a stop of the accounts round.* Then: *Your answer here writes the file again, with the accounts round STOPPED and with no permission to read: look through CoAI: hosts read over ssh, choose “Allow reading…” there, then start the round in AI accounts and usage.* |
| W9 | one title added | The notice `remote-host-refusal-not-kept`, action *Save now* | The two titles as built. Third, a withdrawal: *Your withdrawal of the permission is not saved yet (<why>). This window reads no host until it is; the other windows stop when the shared state reaches them.* Its cure, `disk` or `lost`: *“Save now” writes it at once. Until then only this window and the shared state hold it: keep this window open, and check that the extension’s storage folder can be written.* Its cure, `busy` (B1: the file is made, another window held the hosts file when it was checked): *“Save now” takes the hosts file over from a window that hangs and checks your withdrawal. Keep this window open until then.* |
| W10 | retired with `ignoredRows`, by the commit of S3 or S4 that deletes its last caller (X7; the stories document, E2.S2, *When each goes*) | The ignored layer | — |
| W11 | rewritten | A host over a damaged list or file (the tab draws it) | *the list of hosts cannot be read, so this host is not read. Choose “Not this host” or “Read this host again” for it: that repairs the list.* · *the file of refused hosts cannot be read, so this host is not read. Choose “Not this host” or “Read this host again” for it: that repairs the file, leaves the round stopped and leaves no permission.* |
| W12 | rewritten | **The door answer `waiting`: the session door and the tab** (T8) | *A “Not this host”, a stop or a withdrawal is not saved yet on this side, so nothing was read. Choose “Save now” — the first row of CoAI: hosts read over ssh.* |
| W13 | kept | A start that was not kept | as built |
| W14 | **new** | The ONE question, code `read-accounts-permission` | Title: *Allow CoAI to read the hosts of your Creds terminals?* Detail: *“Allow reading” lets CoAI read every host of an open “Creds: <host> / <session>” terminal with no further question — the ones open now and the ones you open later. It logs in over ssh with your own keys every 10 minutes while one is open. In a Windows window with a “Creds: wsl / …” terminal it also reads every Linux user of each running WSL distro, root too; on a distro with systemd this opens a login session that runs the user’s profile. It reads which of Claude Code, Codex and Antigravity are there, their accounts and usage; claude and agy, as found there, are started (root’s as root), and Claude Code rewrites its cache files. Take or add the question reads that terminal’s Claude session. CoAI itself installs and writes nothing there. To stop: “Not this host”, “Stop the round”, or “Withdraw the permission” for all reads. The permission is for <side> only; another side asks its own.* (Rewritten for owner decision 25, 2026-11-26, and decision 24: see the status block.) |
| W15 | **new** | The session door on `no-permission` | After *Not now* or Escape: *Nothing was read: CoAI has no permission to read hosts. Press again and choose “Allow reading”.* · after *Allow reading* that was kept: *The permission is given. Press again to read.* · at a later door of a press: *The permission to read hosts was withdrawn meanwhile, so the rest was not read.* |
| W16 | **new** | An *Allow* that was not kept: the sentence every caller shows, and for `unwritten` the notice `accounts-permission-not-kept` with the action *Save now* | `unwritten`: *The permission could not be saved (<why>), so nothing changed and nothing is read. Choose “Allow reading” again in a moment.* Its cure, `busy`: *If another window hangs, “Save now” takes the file over; then choose “Allow reading” again.* Its cure, `disk` or `lost`: *Check that the extension’s storage folder can be written; “Save now” tries what waits.* · `withdrawn`: *The permission was withdrawn while it was being saved, so nothing is read. Choose “Allow reading” again if you want it.* · `damaged`: W6's first sentence |
| W17 | **new** | After *Withdraw the permission* in the hosts list (code `no-remote-host-asked`, as every short message of the list) | *The permission is withdrawn: CoAI reads no host. Every side of this machine stops at its next read; “Allow reading” on a side allows that side again.* |
| W18 | **new** | The hosts list: its first row, label and description | none: *Allow reading…* — *no permission yet: nothing is read* · in force: *Withdraw the permission* — *given <the grant's utc>: every host of your Creds terminals is read, but the ones you refused* · waiting: *Save now* — *a “Not this host”, a stop or a withdrawal is not saved yet on this side, so nothing is read* · damaged: *The list or the file of hosts cannot be read* — *nothing is read. Choose a host below and answer for it: that repairs it. If no host is listed, open a “Creds: <host> / <session>” terminal and run this again.* |

**The census.** S1 removes the site `read-remote-session` and adds `read-accounts-permission` and
`accounts-permission-not-kept`: 180 sites after S1 (X9). `node scripts/count-notifications.mjs --write`, and
`PLACES_THIS_SPEAKS` with a dated comment that names the three codes.

## P13 — The test door and the host scenario

**Set by the keeper, restore by raw writes that exist only in test mode** — E1's rule, widened by what is new.

```ts
export interface RemoteSessionsTestDoor {
  // replaceRun, read, pickedOf, readsKey, commit, standings, stopRound, startRound, roundStop — as built
  /** The one door. `admit` is gone. */
  readonly may: RemoteSessions['may'];
  /** The keeper's own *Allow* and *Withdraw*: a scenario gives and withdraws the way a press does, with no modal. */
  readonly allow: HostKeeper['allow'];
  readonly withdraw: HostKeeper['withdraw'];
  readonly permission: RemoteSessions['permission'];
  /** The names of this side's withdrawal files, read now. */
  readonly withdrawnFiles: () => Promise<readonly string[]>;
  /** What is stored, for the `finally`: the list, the state row, the TEXT of the file, and every withdrawal file with its text. */
  readonly stored: () => Promise<{ readonly list: unknown; readonly row: unknown; readonly file: string | undefined; readonly withdrawn: readonly { readonly name: string; readonly text: string }[] }>;
  /** TEST ONLY. Puts all four back by raw writes, removes every other withdrawal file, every marker and the lock, and empties what this window holds and remembers. */
  readonly restore: (before: Awaited<ReturnType<RemoteSessionsTestDoor['stored']>>) => Promise<void>;
}
```

- `restore` and `stored` are the ONLY code of the extension that reads or writes a withdrawal file, the grant or the
  state row's copy without the store and the keeper. They are in `testDoorOf`, which `extension.ts` calls only
  behind `ExtensionMode.Test` (`remoteExportGuard.test.ts`). The scan of A1-T18 is widened: the prefix
  `withdrawn-` is in `remoteSessionHost.ts` once, and no raw file call names it outside `testDoorOf` and
  `remoteRefusalsDisk.ts`.
- `allow` on the test door is the keeper's and not the modal's: S3's and S4's scenarios give the permission
  "through the keeper", as the stories document says. In production no caller but `permissionAsker` reaches it.
- `keeper.forgetHeld()` also drops a withdrawal that this window holds.

**E2S1-H1**, in `src/test/host/terminalHostsScenario.ts`, CI's real-editor job, through the real `globalState` and
the real folder: `before = await door.stored()`; in a `try`: a profile with no grant → `may('session', H)` is
`no-permission`, and `read(terminal)` returns W15's first sentence with the run replaced by one that fails the
test when called; `allow()` → `given`, `permission()` is `{ given }`, `may('session', H)` is `yes`, and
`may('round', H)` is `yes` or `stopped` as the Stop stands; `withdraw()` → `withdrawnFiles()` holds one
`withdrawn-<n>.json`, both doors say `no-permission`, and the state row's `withdrawnStamp` is that n; `allow()`
again → `given`, and `withdrawnFiles()` holds exactly one name, one below the grant's stamp; in the `finally`:
`door.restore(before)`. A1-H2 (the setting through the real configuration) is deleted with the setting.

## P14 — The security judgement

**The edge.** A login with the person's keys to every host of an open `Creds:` terminal, by a timer and by a
press. E1 put a person's "no" in front of it. E2.S1 puts ONE "yes" in front of both doors, and makes its
withdrawal as hard to lose as a "no".

**Is the permission in the right place?** Yes. It is in the one store this feature has that a stale window cannot
write back (F27): the locked file of the side. The withdrawal is not a removal from that file, because a removal
can be undone by whoever still holds an older copy of the file; it is a file of its own, and "in force" is a
comparison that an older copy always loses (I1, I2). `globalState` holds one number that can only take the
permission away.

**Is one "yes" for all hosts, and for the hosts that come later, acceptable?** It is the owner's decision 22, and
the parent's judgement holds: after the permission, any program that can open a terminal named `Creds: <host> / x`
chooses a host that the extension logs in to within 10 minutes (finding F1, as the parent restates it). Three
things bound it: the name chooses the host and nothing else; `ssh` refuses a host whose key is not known and
asks for no password; and W14 says "and the ones you open later" in plain words. This spec adds no condition to
that judgement and removes none.

**What an attacker gains from what S1 adds.**

- **A page of the tab, a timer, a reply from a VM:** nothing. None of them reaches `allow()`, and nothing they
  can send is a button of the modal.
- **A program that can write `globalState` of this extension** (only this extension can; a stale window of it
  does): it can take the permission away on every side, and make the question be asked once more. It cannot give.
- **A program that writes files as the person in the extension's storage folder:** it can write a grant, delete a
  withdrawal, or delete a refusal. It can also edit `known_hosts` and start `ssh` itself. Not a boundary of this
  story (residual 7).
- **Someone on a VM:** nothing here. No value of a reply reaches the file, a withdrawal file, the row or a word.
- **Two windows that race:** the later *Withdraw* wins; the *Allow* that lost is told.

**No state locks the person out without a sentence that names the press that ends it** (P-i):

| The state | What the person sees | The press that ends it |
|---|---|---|
| A withdrawal file that cannot be read | no permission; *Allow reading…* | *Allow reading* removes it (§ P6) |
| The copy is not a stamp | no permission, on every side | *Allow reading* on a side rewrites it |
| The count is at its top | no permission | *Allow reading* starts it again |
| The lock hangs and its owner cannot be shown to be gone | W16 with *Save now* | *Save now*, then *Allow reading* |
| The file or the list cannot be read | W6, W11, W18 | an answer for a host (the mend), then *Allow reading…*, then *Start* |
| A refusal, a Stop or a withdrawal waits | W12, W9 | *Save now* |
| The disk takes no write at all | W16 or W9 with *disk* | none inside the product: nothing can be given on a disk that keeps nothing. It fails closed, and the words say to check the folder |
| A withdrawal came while the *Allow* was saved | W16, *withdrawn* | *Allow reading* again |
| A withdrawal is made, and another window held the hosts file when it was checked (`busy`) | W9's third title; this window says W12 | none is needed when that window ends its write: the next read says *withdrawn*. *Save now* when it hangs |
| An *Allow* on one side whose floor was that side's own older grant raised the copy above another side's grant (N2) | on the other side: no permission, though nobody pressed *Withdraw* | *Allow reading* on that side |
| A withdrawal that waited in another window of the side landed after an *Allow* (N2) | no permission again; nobody is told | *Allow reading* again |

**Judgements.**

- **J-a. A withdrawal wins every tie.** Equal stamps are not in force; a new file during an *Allow* ends it; a
  thing that cannot be read counts as withdrawn. Each costs at most one more press of *Allow reading*, and that
  press is always shown.
- **J-b. An unreadable withdrawal is ended by a person's *Allow*, not by a rule.** No automatic path removes one,
  whatever its age or its name.
- **J-c. A withdrawal reaches every side, and a grant none but its own** (K5). The owner uses a second side for
  the chat only; withdrawing in the Windows window now also stops the chat doors of a WSL window, which is what
  "withdraw" says.
- **J-d. An *Allow* never takes the lock over, and never waits in a queue of its own.** A widening is not forced
  and not retried. The way past a lock that hangs is a second, separate press (*Save now*).
- **J-e. The first press of a chat door after the permission reads nothing.** The person allowed the reading; the
  press that asked is not carried through the modal, so no modal answer ever spawns `ssh` in the same step.
- **J-f. `regain` and `mend` give nothing.** After a stale rename or a mend the permission can be missing; it is
  never present by a write that no person pressed.

**Residuals.** 1–8 are the file's eight (the E1.S1 spec, § S1.10), as they stand after this story; 9–12 are the
parent's; 13–19 are this story's. E1's own 11 and 12 (the setting written by other code; the first contact
before any telling) are GONE with the setting and with the permission.

1. **A call that is already running** when a refusal, the Stop or a withdrawal is written runs to its end, 30 s
   at most; its result is dropped. A call that starts afterwards is not made, in any window of that side.
2. **Another side learns of a refusal or of the Stop through its copy only**, as E1 said. For a withdrawal:
   residual 10.
3. **A person's "yes" that a stale write puts back allows nothing any more.** What is left of this residual: such
   a row is shown again in the hosts list as *stored and not used*.
4. **A lifted refusal can come back**, as its copy and through the copy-in, as E1 said. It errs to not
   contacting; W3 says to choose once more.
5. **The lock, the marker and the writer**, letters as E1: (a) a lock whose owner cannot be shown to be gone
   makes every write of the side `busy` — an *Allow* too — until *Save now*; a withdrawal's file is still made,
   and its window says `waiting` until then; (b) after *Save now* the former holder can still rename its stale
   file: a refusal comes back by `regain`, **a grant does not** — the permission is then the stale file's, an
   older one or none, never one that was withdrawn. **One case is left of that "never"** (B1): the former holder
   was an *Allow* past its last look at the claim (§ P6, step 7); a window that had not seen the copy yet (V13)
   took its withdrawal's stamp before that *Allow*'s floor file was made, and read back after the lock was taken
   from the *Allow*. The *Allow*'s rename then puts a grant above that withdrawal, and only the *Allow*'s own
   read-back takes it back — so the grant stands when that window ends between its rename and its read-back;
   (c) two windows that both judge one owner gone; (d) a disk that takes neither the file nor a marker.
6. **A damaged file or list** stops every read of that side until a person mends it, and no notice says so. A
   mended file has no permission.
7. **A file deleted or edited from outside.** A deleted `refused.json` is a file with no permission. A deleted
   withdrawal file leaves the withdrawal in the copy, which is copied in again while it stands. A program that
   writes a well-made grant gives a permission. **And the restart at the top** (§ P5): an *Allow* that finds its
   floor at `STAMP_MAX` removes every withdrawal file, so I1 and I2 do not hold across it — a stale `refused.json`
   with an older, higher grant then beats a later withdrawal. Only a file made by hand reaches the top. Not a
   boundary of this story.
8. **A window still running an older build** reads a host with an old "yes" row on a press with no permission,
   and after a withdrawal; the pre-release build reads by its own round. Until that window is reloaded.
9. **A withdrawal is files on one disk** (the parent's 9). A stale writer of `refused.json` cannot undo it. What
   can: a disk that takes no withdrawal file — it then holds in the pressing window and as its copy, there is no
   marker for it (J2), and W9 says to keep the window open — and a program that deletes files as the person.
   **And two windows that both end** (B1): a withdrawal of a window that had not seen the copy yet (V13), made
   while an *Allow* of another window was being written, is below that *Allow*'s grant until one of the two
   read-backs has run. When the *Allow*'s window ends after its write AND the withdrawing window ends before its
   own read-back is final — its door said `waiting` until then — the grant stands, and nobody is told.
10. **The permission is given for one side; a withdrawal reaches every side that sees its copy** (the parent's 10,
    amended: A1). Left, two cases, both a stale write of the shared state (F27): (a) it lowers or erases the copy
    AFTER the withdrawal, before the other side has read it — the other side then reads on until a window of the
    withdrawing side reads again, which puts the copy back; (b) it lowers the copy BEFORE the withdrawal, and the
    other side has not read since — the withdrawal's stamp is then taken from too low a copy and can be below the
    other side's grant, so that side reads on until it is withdrawn there. Nobody is told in either case. The side
    where the person pressed is withdrawn in both. The lag is not measured (V13).
11. **20 hosts are 20 logins every 10 minutes** (the parent's 11; the round's).
12. **A stopped distro** (the parent's 12; E3's).
13. **A hundred refusals**: the next "no" is not kept, and the person is told (E1's 9).
14. **A spawn in flight** is not stopped: at most ONE more call of a press reaches a host after a "no" or a
    withdrawal, the one that was already running (E1's 10, narrowed by T9 from three to one).
15. **A side where something waits reads no host at all** (E1's 13), now also for a withdrawal: every window that
    has seen the copy. A window that has not seen it yet reads on until it does, when the disk took no file.
16. **A stale rename after an *Allow*, or a mend, can leave the side with an OLDER grant of the same person** that
    was never withdrawn — or with none. Never with one that a withdrawal stood above.
17. **An unreadable withdrawal file that an *Allow* removes may have been a real, earlier withdrawal.** The
    *Allow* is the person's later word; the file's own text is lost, and nothing records that it was there.
18. **An *Allow* on one side can end an unreadable copy for every side**, and a side whose own grant is above the
    rewritten copy reads again without a press there (§ P5, the last cell).
19. **The words of W14 are wider than E2's build.** A person who allows in E2's pre-release has allowed the WSL
    read and the usage read of the later epics. That is meant (J10); the owner is the only one who runs that
    build.

**`SECURITY.md`** is S5's. From this spec it takes: that no host is contacted before the one permission, by
either door; where the permission is kept and that nothing in `globalState` can give it; the withdrawal file, the
stamp rule in one sentence, and why a stale writer cannot undo it — with "the writer claims, writes and reads
back", never "cannot be lost"; that a withdrawal reaches every side and a grant one; that a thing that cannot be
read counts as withdrawn; that only a person's press gives; residuals 1–19.

## P15 — Scenarios

Unit tests run by `node --test` through `scripts/run-tests.mjs`; each file starts with
`import '../../scripts/inRunFolder.cjs';`. **RED first for every row**: the test is written before its code and
seen RED — a compile error counts for a symbol that does not exist yet — then GREEN, then RED again with its
break-it of § P16 applied to the PRODUCTION line, then GREEN. Every observation is in the pull request.

**The fakes.** E1's `backing`, `view(backing, mode)` with `'whole-state'` and `sync()`, and its fake disk with an
exclusive create, a rename and a clock. The fake disk gains the withdrawal half and **records every call** by its
name and argument (`withdrawn`, `unwithdraw`, `replace`, `sweep`, `unmark`, `lockNow`), and a lock that a test
sets to `free`, `held` or `gone` — a window that was disposed while it held the claim leaves it `gone`. `side()`
builds one folder;
`keeperOn(side, view)` builds a keeper as a reload does. A *stale holder* is a writer that was given the claim,
read the file, and is frozen before its rename. `door(k, who, h)` is `k.may(who, h)`; `calls` counts what a fake
run or a fake accounts call received.

| # | What is shown | Cells | File |
|---|---|---|---|
| E2S1-T1 | The grant record | **Parse**: the table of § P3, row by row; a file without `grant` has no permission; a malformed `grant` is no grant AND the file is not damaged (its refusals and its Stop still read). **Through a write**: for every change kind but `grant` and `mend`, over generated files, the stored `grant` value — well made or not — is the same value after the write; after a `mend` there is none; after a `regain` it is the grant of the file the change was merged into, never the remembered one. RED first: `grantOf` does not exist | `remotePermission.test.ts`, `remoteRefusals.test.ts` |
| E2S1-T2 | The stamp rule, as a table | Every row of § P5's table, through `inForce`; and through the door for the first six | `remotePermission.test.ts`, `remoteKeeperDoor.test.ts` |
| E2S1-T3 | *Allow* | No withdrawal → stamp 1, no file. Grant 1 and `withdrawn-2.json` → stamp 3, and **exactly one** withdrawal file is left, `withdrawn-2.json`, read back. Files 2, 5 and 7 → stamp 8, file 7 left. The floor from the copy alone (copy 9, no file) → `withdrawn-9.json` is made, stamp 10. **The pure merger refuses a stamp that is not above its floor** (`low`), and a `grant` change cannot be sent through `commit` (a type test). A file whose grant is in force → `kept`, **zero** calls of `replace`, `withdrawn` and `unwithdraw`. The read-back: the grant is there and in force. **Two *Allows* at once**: the second meets the first one's floor file as a new name, finds the grant in force and answers `kept`, not `withdrawn` | `remoteRefusals.test.ts` |
| E2S1-T4 | *Withdraw* | One above the highest stamp seen (the grant, a file, a name of an unreadable file, `above`). Made by `withdrawn` — an exclusive create; `there` is no error. **Over the recorded calls of every other operation** — `refuse`, `lift`, `stop`, `start`, `migrate`, `regain`, `mend`, the sweep, `saveWaiting` — no `unwithdraw` and no `replace` of a name with the prefix. With 8 files the LOWEST readable goes first, never the highest, never an unreadable one; with 8 unreadable files none is made and the answer is `withdrawn`. No call of `claim`. **The read-back and the lock** (B1): in the recorded order `lockNow` comes before the listing of each read-back; the lock `held` and then `free`, with a grant written in between → the withdrawal goes again from 4 and ends above the grant; the lock `held` to the end → `unwritten: busy`, the file of step 8 is in the folder, and no `claim` was recorded; the lock `gone` → the read-back stands. The name is built from the handed-in prefix: a store over a disk with another prefix makes and reads names of that prefix (F2) | `remoteRefusals.test.ts` |
| E2S1-T5 | **The stale lock holder** | *Allow*; a stale holder reads the file (it holds the grant) and freezes; *Withdraw*; the window is disposed; the holder renames; a new keeper → both doors `no-permission`, `calls` 0. The same with *Allow*, *Withdraw*, *Allow*, *Withdraw* and a holder from each earlier point. The mirror: the holder's file has NO grant and the last word is *Allow* → `no-permission` (never `yes` by `regain`), and the next *Allow* is `given` | `remoteKeeperDoor.test.ts` |
| E2S1-T6 | **The stale window** | A withdraws; A is disposed; stale B writes the WHOLE state (the copy gone, `'whole-state'`); A and B are built again → no `yes`. Then *Allow* after the withdrawal; B's stale write (an older, lower copy) → still `yes`: a stale write does not take an *Allow* back. A stale write that brings back a "yes" row → no `yes` without the permission | `remoteKeeperDoor.test.ts` |
| E2S1-T7 | A withdrawal the disk did not take | The fake `withdrawn` answers `refused`. **The row's change is recorded BEFORE the first disk call.** This window: `waiting` on both doors for a host that is not refused, `no` for a refused one. A second window of the side, after `sync()`: `waiting` on both doors. A second window that has not seen the copy: recorded as it is (residual 15). *Save now* with the disk healed → the file is there, both windows `no-permission`. **An *Allow* the file did not take** (`busy`, `disk`, `lost`): the file's bytes are the same, nothing is held, `held()` is unchanged, the result is `not-kept` with W16. **An *Allow* over a held withdrawal** (F4): a withdrawal held, the copy erased by a stale write, *Allow* → the store says `kept`, the held withdrawal is dropped, and the door says `yes` — also after the next read; the same with `given`. **A withdrawal that is `busy`** (B1): the door says `waiting` in its window and `no-permission` in a second window of the side | `remoteKeeperDoor.test.ts` |
| E2S1-T8 | The copy | Copy 5, a folder with a grant 5 and no withdrawal file: the door says `no-permission` at once, and after that call the folder holds `withdrawn-5.json`. The folder's highest is 7 and the copy 5 → the copy is 7 after one read. **The copy only grows**: over generated sequences of `{ withdrawn }` changes the stored stamp never falls; only `{ floor }` over a value that is not a stamp, or with `restart`, replaces it | `remoteKeeperDoor.test.ts`, `remoteAccountsRound.test.ts` |
| E2S1-T9 | **The one door** | Every row of § P8's first table, for both callers, each built so that ONLY that row holds; then the order — two rows hold, and the earlier one is the answer (`no` over `damaged`, `damaged` over `waiting`, `waiting` over `no-permission`, `no-permission` over `stopped`). **The Stop**: `stopped` for the round, `yes` for the session. **The scan** (`remoteHostsWiring.test.ts`): `inForce(` is called in `remoteKeeper.ts` and `remoteRefusals.ts` only; the key `grant` is read in `remotePermission.ts` and written in `remoteRefusals.ts` only; `'withdrawn-'` is in `remoteSessionHost.ts` once; no source file holds `admit(`, `.consent(`, `admitHost`, `askHost`, `askQuestion` or `read-remote-session` (but the bridge line, by name); `keeper.allow(` is in `remoteSessionHost.ts` only, in the binding of `permissionAsker` and in `testDoorOf`. The scan fails when it matches nothing it should | `remoteKeeperDoor.test.ts`, `remoteHostsWiring.test.ts` |
| E2S1-T10 | **A per-host "yes" allows nothing** | A list with a person's "yes" for h and an automatic row for g, no permission. *take* and *add* on h and on g: `askPermission` is called once for each press, the run is called **0** times. *Not now* → W15's first sentence. *Allow* → W15's second, **0 runs in that press**; the next press runs. After a withdrawal: a press runs nothing and asks again. **The stored list is the same value after 100 presses**, and the recorded writes of `coai.terminalHosts` are 0. Carries CUT-H1, H2, D1 (both purposes), D2–D5: each assertion is named beside the line that now holds it | `remoteRead.test.ts`, `remoteHosts.test.ts` |
| E2S1-T11 | **The upgrade** | Every row of § P11's table. The Stop: with `stop` in the file, `allow()` is `given` and `may('round', h)` is `stopped`; after *Start* it is `yes`. A list with a "yes" row and a `how: 'auto'` row: `readableOf(…).damaged` is false | `remoteKeeperDoor.test.ts`, `remoteHosts.test.ts` |
| E2S1-T12 | **T8** | A held refusal for h, a press on g → W12, word for word, and never W6. A held withdrawal → W12. A waiting marker of another window → W12. A torn marker → W12. A damaged list → W6; a damaged file → W6 | `remoteRead.test.ts` |
| E2S1-T13 | **T9** | The fake run saves a "no" for the host during the first spawn → the titles fallback does not spawn (1 run), and the answer is *turned off*. A withdrawal saved then → 1 run, W15's third sentence. The door still `yes` → the fallbacks run (2 and 3 runs; S3-T13's companions unchanged). **`askPermission` throws when called**: no later door calls it. A pick that is open while a "no" is saved → no spawn after it | `remoteRead.test.ts` |
| E2S1-T14 | The question | `askFirst`: the first press raises the modal, the second does not; `noteAsked` is recorded BEFORE `raise`; a row without `askedUtc` asks once more. `ask`: no modal when the permission is in force; *Not now* writes nothing (0 calls of `allow`, of the row, of the disk); two calls while one is open → one `raise`. **A tick never asks**: `RoundSeams` and `KeeperSeams` have no member that reaches `raise` (a type test), and a round run with a `raise` that throws ends without a throw. The code, the title, the detail and the two buttons equal W14. `read-remote-session` is in no source file and not in `notification-sites.json` | `remotePermission.test.ts`, `remoteHostsWiring.test.ts` |
| E2S1-T15 | The hosts list | The first row in each of its four states, label and description equal W18, and the press each maps to. The hosts: refused ∪ live ∪ stored, in code-unit order. **The buttons by state**, the table of § P10, and the change each sends. No button *Read*; no change can carry `answer: 'yes'` (a type test). A stored "yes" row: W4 and W4b | `remoteHosts.test.ts`, `remoteHostsWiring.test.ts` |
| E2S1-T16 | **The sides** (K5) | Two sides, ONE state. *Allow* on A → B is `no-permission`, and B's folder holds no grant. *Allow* on both; *Withdraw* on A → B's next door is `no-permission` and B's folder then holds a withdrawal file. *Allow* on A → A `yes`, B still `no-permission`. *Allow* on B → `yes`. **No call of B's `replace` is recorded between A's *Allow* and B's own**: a grant is never copied | `remoteKeeperDoor.test.ts` |
| E2S1-T17 | The setting is gone | Not in the manifest; `SAFETY_KEYS` is `['kimiAllowNetworkSignIn']`; not in `NEVER_TRANSFERRED`; an imported file that holds the key is refused *unknown to this build*; the key-read scan of `modelKeysAreReadOnce.test.ts` finds no read; the key's text is in `src/` in the ONE bridge line (X6) and in the five help files, which S5 rewrites (E2S5-T2 removes this allowance), and nowhere else — **but for one more line that the code needs** (G16, X13): the constant `KEY` of `remoteHostsSaid.ts`, from which W10's two sentences are built until W10 is deleted (X7); the scan allows that line by name, with a companion that it is used by W10's two sentences only, and the commit that deletes W10 removes the allowance; a user layer that holds `true` changes no door answer | `modelKeys.test.ts`, `configTransfer` tests, `remoteHostsWiring.test.ts` |
| E2S1-T18 | Retired code stays gone | `{ kind: 'auto', … }` and `answer: 'yes'` do not compile as a `HostChange`; `AUTOMATIC_ALLOW`, `autoCounts`, `AutomaticAllow`, `allowNew`, `automatic` are in no source file; the parser still reads `how: 'auto'` | `remoteHosts.test.ts`, `remoteHostsWiring.test.ts` |
| E2S1-T19 | **K4 — a withdrawal file that cannot be read** | Each row of § P4's table reads as written; with one in the folder both doors say `no-permission`. *Allow*: it is read three times and once more, then removed; the floor file is made BEFORE the first `unwithdraw` (the recorded order); the grant is written AFTER the last one; the result is `given`. **A file that appears after the *Allow* began** (the fake disk adds it between the two listings, and again between the write and the read-back) → `withdrawn`, that name is never in an `unwithdraw`, and both doors say `no-permission`. An unreadable file that cannot be removed → `unwritten: disk`, **0** calls of `replace`. A file that is empty at the first read and whole at the second is counted as a stamp and not removed. `crowded` (20 names): not in force; an *Allow* removes them. The floor's name is taken by an unreadable file → the floor is one higher. **A later press that was listed** (F5): a readable withdrawal whose `utc` is later than the *Allow*'s is in the folder BEFORE step 0's listing (the *Allow* waited in the queue) → `withdrawn`, 0 calls of `replace`; with a `utc` that is earlier → `given` | `remoteRefusals.test.ts`, `remotePermission.test.ts` |
| E2S1-T20 | **A withdrawal wins, over generated histories** | A generator of histories over one side and one state: *Allow*, *Withdraw*, a stale rename of any earlier text of `refused.json`, a stale whole-state write, a reload, a `mend`, a withdrawal while `refused.json` cannot be read, an *Allow* and a *Withdraw* interleaved at every step; **and** (B1) **the *Allow*'s window dying between steps 8 and 10 — its claim's owner gone — with a withdrawal of a window that had not seen the copy** (a second view of the state, behind the first), **a step-10 listing that fails, and an *Allow* whose claim is taken by a person's press after its write** (it starts again, and must not end as `kept`). **After each history whose last press is *Withdraw*, no door says `yes`; whenever a door says `yes`, a press of *Allow* came after the last *Withdraw*.** A history in which BOTH windows of such a race are disposed before a read-back is not generated: it is residual 9. And I1: after every `given`, the folder holds a readable withdrawal one below the grant, or the grant is 1 | `remoteKeeperDoor.test.ts` |
| E2S1-T21 | The copy that is not a stamp, and the top | A string, a fraction, 0, a negative, an object, 10¹² + 1 → `'unreadable'`, both doors `no-permission`, and **no `{ withdrawn, by: 'read' }` replaces it**; an *Allow* does, and is `given`. A row that cannot be read at all: stopped and no permission; after an *Allow* the row is readable and STILL stopped. The floor at `STAMP_MAX`: an *Allow* restarts at stamp 1 and is `given`; a withdrawal at the top takes `STAMP_MAX` and wins | `remoteAccountsRound.test.ts`, `remoteKeeperDoor.test.ts` |
| E2S1-T22 | ***Save now*** | With nothing waiting and a lock whose note cannot be read: `saveWaiting()` records ONE claim with `take: 'person'` and no `replace`; the next `allow()` is `given`. With a withdrawal that waits: it lands it. **With a withdrawal that is `busy` behind a lock whose note cannot be read**: the claim with `take: 'person'` is recorded BEFORE the withdrawal's `lockNow`, and the answer is `withdrawn`. `take: 'person'` reaches the claim from `saveWaiting` and `stopAfterTorn` only, and no file of the round calls `saveWaiting` (E1's scan, kept) | `remoteKeeperDoor.test.ts`, `remoteHostsWiring.test.ts` |
| E2S1-T23 | The list after the "yes" | A write of the list leaves out every `how: 'auto'` row and keeps every other row by reference. At 100 rows a person's "no" pushes out the oldest "yes" row; with 100 "no" rows it is `full`. Over generated lists and changes, no write adds a row whose `answer` is `'yes'` | `remoteHosts.test.ts` |
| E2S1-H1 | The host scenario | § P13 | `host/terminalHostsScenario.ts` |

**Tests of E1 that are replaced, each named in the pull request**: the automatic-row and eviction cases of
`remoteHosts.test.ts` (A1-T3's automatic half, A1-T4, A1-T7's automatic cells); A1-T12's question cells in
`remoteRead.test.ts` (S3-T3's "no ssh before a yes" becomes T10); A1-T14's rows for W2, W5 and the setting's
sentences; `remoteHostsCut.test.ts` and `terminalAccountsSetting.test.ts` whole; A1-H2; and (gate E2-G1, F3) the
scans of the setting's seam in `remoteHostsWiring.test.ts:73-75` and its `KEYSAID` at `:83`, the safety-keys case
of `modelKeys.test.ts:67-68`, and the planted read of the key in `modelKeysAreReadOnce.test.ts:50`.

**E1's tests of the file, the lock, the markers, the copy-in and `regain` stay green**: `keeper.admit`/
`keeper.consent` become `keeper.may(…)` (`may('round', h).then(admissionOf)` where a test hands the round its
seams), and a fixture whose assertion expects a host to be read gives the permission first through the keeper's
`allow()`. Each test whose expectation changed is named in the pull request. They are many — with no grant `may`
answers `no-permission` where E1's door read: `remoteHosts.test.ts` has 102 door calls, `remoteHostsReview.test.ts`
14, and `remoteAccountsState.test.ts:34`, `:115`, `:128` and `:207` hand the round `keeper.admit`.

## P16 — Break-its (a PRODUCTION change; each seen RED with its message, then restored)

| # | Change | Goes red |
|---|---|---|
| R1 | `refusalsOf` builds the object from its known keys | T1 — *a write dropped the grant* |
| R2 | A malformed `grant` makes `refusalsOf` answer damage | T1 — *a bad grant stopped the refusals from being read* |
| R3 | `regain` writes the remembered grant | T1, T5 (the mirror) — *a file that went back gave a permission no press gave* |
| R4 | `mended` spreads the old file | T1 — *a mend kept a grant* |
| R5 | `inForce` compares with `>=` | T2 — *grant 1 with withdrawal 1 was in force* |
| R6 | `inForce` skips an `unreadable` file | T2, T19 — *a withdrawal file that cannot be read did not withdraw* |
| R7 | `inForce` does not look at the copy | T2, T8, T16 — *a withdrawal that is only in the copy did not withdraw* |
| R8 | The grant's merger does not compare with the floor | T3 — *a grant was written at or below a withdrawal* |
| R9 | An *Allow* removes every withdrawal file | T3, T5 — *no floor was left; a stale file's grant was in force after a withdrawal* |
| R10 | An *Allow* takes its listing after the claim only | T19 — *a withdrawal made while the Allow waited was overridden* |
| R11 | An *Allow* removes by prefix, not by the names it listed | T19 — *an Allow removed a withdrawal that came after it began* |
| R12 | The read-back of an *Allow* does not list again | T19 — *an Allow reported given over a newer withdrawal* |
| R13 | A withdrawal takes the highest stamp, not one above | T20 — *a withdrawal equal to a grant in a stale file's successor did not hold* |
| R14 | A withdrawal's read-back is removed | T20 — *a withdrawal reported saved under a grant written meanwhile* |
| R15 | Pruning removes the highest stamp | T4, T20 — *the newest withdrawal was removed* |
| R16 | A writer of `refused.json` sweeps names with the prefix | T4 — *the sweep removed a withdrawal* |
| R17 | The row's change is awaited after the disk | T7 — *the copy was written after the file was asked* |
| R18 | `waiting` is answered after `no-permission` | T9, T12 — *a waiting side asked for the permission* |
| R19 | The door answers `damaged` for a waiting side (E1's `waitingConsent`) | T12 — *W6 was said for a list that can be read* |
| R20 | `consentOf` answers `yes` for a "yes" row and the door returns it | T10, T11 — *a per-host “yes” read a host with no permission* |
| R21 | `readOnce` asks the door only when `trip.calls` is 0 | T13 — *a second spawn went out after a “no”* |
| R22 | A later door calls `askPermission` | T13 — *a fallback asked a person* |
| R23 | The road spawns after *Allow reading* in the same press | T10 — *the press that asked also read* |
| R24 | `askFirst` raises before it notes | T14 — *the icon asked twice* |
| R25 | The copy-in is skipped | T8, T16 — *another side's withdrawal was not written into this side's folder* |
| R26 | An *Allow* copies the grant to the row and a door reads it there | T16 — *a permission of one side was one of another* |
| R27 | A `{ withdrawn, by: 'read' }` replaces a copy that is not a stamp | T21 — *a read ended a withdrawal* |
| R28 | `saveWaiting` does not call `free` | T22 — *a lock that hangs kept the permission from being given* |
| R29 | A person's change may carry `answer: 'yes'` | T15, T18, T23 — the type test fails to fail |
| R30 | The Stop is answered for the session | T9 — *a stopped round stopped a chat door* |
| R31 | Step 9 of § P7 ignores a held claim | T20 — *an Allow that died after its write outlived a later withdrawal* |
| R32 | An *Allow* removes the unreadable files before it makes the floor file | T19 — *the old grant was in force between a removal and the floor* |
| R33 | An *Allow* that has written answers `kept` when it starts again (§ P6, step 9) | T20 — *an Allow whose claim was taken ended as kept over a later withdrawal* |

## P17 — The brief: order, files, commands

**Before step 1**: gate E2-G1 has passed and the caller has checked its changes in this text; the stories
document's § 4 holds X1–X12 and the document has taken X13–X16; the caller has decided the rows of *Parent text to
amend*. The branch has `origin/main` merged (the stories document, § 6, step 1).

**The bridge — so that S1 lands alone with the compile green.** `remoteAccountsRound.ts` (but its state row),
`remoteAccountsRows.ts` and the list half of `remoteAccountsHost.ts` are S4's, S2's and S3's, and they still read
three things S1 retires. S1 makes exactly these edits there, and no other:

| Where | The edit | Who removes it |
|---|---|---|
| `remoteHosts.ts`: the type `Admission` | Stays as it is, with the doc line *the round's old view of the door; deleted by E2.S4*. NEW pure `admissionOf(answer: DoorAnswer): Admission` beside it: `yes` → `read` (`how: 'person'`, `allowedNow: false`), `no` → `refused`, `damaged` → `damaged`, `waiting` → `unwritten`, `no-permission` → `not-asked`, `stopped` → `unwritten` | S4, with the type |
| `remoteAccountsHost.ts:140` | The second arm becomes `this.sessions.may('round', host).then(admissionOf)`. The first arm — the cut's lock — is not touched | S4 |
| `remoteAccountsHost.ts:342-343` | `ignored: undefined`, `allowNew: false` | S3, with the list |
| `remoteAccountsHost.ts:445` | `ask: (host) => sessions.changeHost(host)` | S3, with the list |
| `remoteAccountsHost.ts:4`, `:379` | The import goes; `SETTING` is the string literal, with the doc line *the retired key, named only in two notices that E2.S4 deletes* | S4, with the notices |
| `remoteHostsSaid.ts`: W10 | `ignoredWorkspace` and `ignoredFolder` stay, with the doc line *deleted with `ignoredRows`*; the constant `KEY` they are built from stays with them (X13) | S3 or S4, with `ignoredRows` |
| `remoteAccountsState.test.ts` | The round's `admit` seam → `may('round', h).then(admissionOf)` (`:34`, `:115`, `:128`, `:207`) | S4, with the round |

**While the cut's `ACCOUNTS_READING` is `off` — until S4 deletes it — the round never runs, so no build executes
the bridge.** The reviewer checks that S1 did not change that constant.

One commit for each step; `npm run compile` exit 0 and the whole suite green at each.

1. **The pure rules.** NEW `src/remotePermission.ts`: `STAMP_MAX`, `stampOf`, `Grant`, `grantOf`, the withdrawal's
   name, text and parser (the prefix is a parameter), `Against`, `inForce`, `floorOf`, the withdrawal's stamp, the
   types of the door and of the question, `permissionAsker`. T1 (parse), T2 (the pure half), T14 (the pure half),
   T19 (the parse half).
2. **The file.** `remoteRefusals.ts`: `grant?: unknown`, the `grant` change and its merger, `low`, `holds`; the
   disk seam's withdrawal half, `withdrawnPrefix` and `lockNow`; `RefusalsRead.withdrawn`; the store's `allow`,
   `withdraw`, `copyIn`, `free`. `remoteRefusalsDisk.ts`: the real half, the prefix handed in; the ONE export of
   `chatStoreLock.ts` that `lockNow` reads through (§ P4). T1 (the writes), T3, T4, T19, and T22's store half,
   with the real exclusive create in a temporary folder for one cell of T4.
3. **The row.** `remoteAccountsRound.ts`: `permissionRowOf`, the three changes, `fieldsOk` for `askedUtc`. T8 (the
   merge half), T21 (the row half).
4. **The list.** `remoteHosts.ts`: the narrowed `HostChange`, `consentOf` as the refusal half, the automatic "yes"
   out, the write that leaves out automatic rows, the cap. `admissionOf` (the bridge). T11 (the list half), T18,
   T23.
5. **The keeper.** `remoteKeeper.ts`: the row seam; `may`, `allow`, `withdraw`, `permission`; the pending
   withdrawal, the copy-in and the copy-out in `sync()`; `saveWaiting` with `free`; `admit`, `consent`,
   `allowNew`, `automatic` out. Should the file pass 800 lines, the permission's operations move to a NEW pure
   `src/remoteKeeperDoor.ts` as functions over a narrow view of the keeper; no rule moves. T2 (the door half),
   T5–T9, T16, T20, T21, T22.
6. **The road.** `remoteRead.ts`: the seams, the door in `readOnce`, the sentences. T10, T12, T13. **This commit
   deletes the two todo files T8 and T9 and their rows in `todo/README.md`** (the stories document).
7. **The setting.** `package.json`, `modelKeys.ts`, `sideConfig.ts`, `configTransfer.ts`, the help census's alias,
   the one help sentence the census needs. T17.
8. **The host half.** `remoteSessionHost.ts`: `WITHDRAWN_PREFIX`; the keeper bound with the row seam; the modal;
   `permissionAsker` bound; `askPermission`, `askPermissionFirst`, `withdraw`, `permission`, `may` on
   `RemoteSessions`; the hosts list with its first row and the buttons by state; the notice of W16; W9's third
   title; the test door. `remoteHostsSaid.ts`: the words. The bridge lines. The census. T14, T15, T9's scan.
9. **The host scenario.** § P13. It runs only in CI's real-editor job; its result is read from that job.

Lint: `complexity` 4, 50 lines for a function, 800 for a file; no suppression is added. The steps of § P6 are one
small function each behind the writer's loop, as E1's eight are; the door's table is a list of predicates in the
order of § P8, not a chain of `if`.

Commands, from `src_vs_code/`: `npm ci`; `npm run compile` (read the exit code); `node
../.agents/conventions/tools/low-priority.mjs node scripts/run-tests.mjs out/test/remotePermission.test.js`, the
same for `remoteRefusals`, `remoteKeeperDoor`, `remoteHosts`, `remoteRead` and `remoteHostsWiring`; `node
../.agents/conventions/tools/low-priority.mjs npm test`; `npm run lint`; `node scripts/count-notifications.mjs
--write`. From the worktree root: `node .agents/conventions/tools/plan-lifecycle.mjs`.

## P18 — Growth

- **The grant**: one key of `refused.json`, under 100 bytes; the file's caps stand (100 refusals, 64 KiB).
  **Interrupted:** the file is the old one or the new one.
- **The withdrawal files**: written under 100 bytes each; 256 is the cap of the read. **At rest after an
  *Allow*: one** (the floor), or none after the first *Allow* of a side. **Between two *Allows*:** one more for
  each *Withdraw* press and each copy-in — and for each try of a withdrawal that waits, which takes its stamp anew
  (a press that goes again from 4, a read while it is `busy`) — at most 8: the ninth first removes the lowest.
  **Who retires them:** every *Allow* (all but its floor), and the pruning. **Interrupted:** an exclusive create
  leaves the file or no file; a file that cannot be read is a withdrawal until an *Allow* removes it. A folder filled from outside is not read beyond 16 files at a call, and
  an *Allow* removes 1024 names in a press.
- **The state row**: three fields for S1 (`stoppedUtc`, `withdrawnStamp`, `askedUtc`), under 150 bytes with S4's
  one. It is written at a *Withdraw*, at an *Allow*, at the icon's first press, and by a copy-out — only when the
  folder is ahead of the copy, so a read with nothing to copy costs no write.
- **`coai.terminalHosts`**: 100 rows at most; S1 writes "no" rows and removals only; automatic rows go at the
  next write.
- **The door**: one read of the file, one listing and at most 16 small reads for each call, nothing kept.
- **Memory of a window**: one pending withdrawal; E1's held refusals and its memory of the newest file.
- **Notices**: one for each *Allow* that was not saved; one for a withdrawal that waits, told once.
- **On a VM**: nothing.

## P19 — What the coder must not do

1. **Do not add a second reader of the grant, of a withdrawal file or of the copy.** `grantOf`, `withdrawalOf`,
   `permissionRowOf`, and `inForce` over them. A door, a model or a word that needs "is it allowed" asks `may` or
   `permission`.
2. **Do not write a grant anywhere but step 8 of § P6**, and do not call `allow()` from anything but
   `permissionAsker` and the test door. Not from a page message, a timer, a read, `regain`, `mend`, `saveWaiting`
   or a retry.
3. **Do not put the grant, a "permission given" flag or its time into `globalState`**, into a setting, into a
   context key, or into a reply to the page that a later read trusts. The row holds a withdrawal's stamp only.
4. **Do not remove a withdrawal file by its prefix, its age or its size.** Only by a name from the listing of the
   *Allow* that removes it, or as the lowest stamp of the pruning.
5. **Do not make "in force" true by default** — for a file that is missing, a listing that failed, a value of the
   wrong type or a `catch`. Every doubt is "not in force" or `damaged`.
6. **Do not hold, mark or retry an *Allow*.** A widening that did not land is told and forgotten.
7. **Do not give an *Allow* `take: 'person'`**, and do not take a lock over by its age.
8. **Do not ask a person from the keeper, the round or a later door of a press.** No `raise` seam goes into
   `KeeperSeams` or `RoundSeams`.
9. **Do not spawn in the press that asked the question**, whatever its answer.
10. **Do not write a "yes" row, and do not read one as consent** — not as a fallback while there is no file, not
    for a host "that was allowed before".
11. **Do not delete or rewrite a stored "yes" row at activation.** Rows change only by a person's answer for
    their host, or as J7 says at a write.
12. **Do not put a path, a file's text or a disk error into a notice, a sentence or a log line** of the
    `remote*.ts` modules.
13. **Do not change `ACCOUNTS_READING`, the round's rhythm, the script, the runner or `SSH_OPTIONS`**, and make no
    edit in `remoteAccountsHost.ts`, `remoteAccountsRows.ts` or the round but the bridge lines and the state row.
14. **Do not add a setting, a command, a marker kind, a file kind or a notice** that this spec does not name. A
    thing that seems to be missing is a question to the caller, before it is code.
15. **Do not skip a RED.** A test that was never seen red proves nothing about a "never".
16. **Do not make a withdrawal's FILE wait for the lock, and do not let `lockNow` change anything.** It reads. A
    held lock delays only the word *withdrawn*; it is never taken over or aged by a withdrawal. In
    `chatStoreLock.ts` nothing changes but the one export.

## P20 — What the Opus reviewer checks

**At gate E2-G1, over this text**: the eight rows of *Parent text to amend* against the parent, and whether the
caller's count is right; J1–J11; that every answer of § P8 has a caller's action and a sentence; the steps of
§ P6 and § P7 against M1–M10, with one interleaving written out for each; that X1–X12 are complete — a search of
`src/` for every symbol of the stories document's *Removed* list, as X5–X7 were found.

**Over the code**:

1. `inForce` is the one comparison, strict, and every input that cannot be read is against the grant — read in
   the code, not from the test alone.
2. The grant is written in one place; `allow()` has two callers; nothing in `globalState` is read as a
   permission.
3. The steps of § P6 in their order; the claim released on every path; the listing of step 0 taken before the
   claim; removals by listed names only; the floor made before the first removal and the grant written after the
   last; a later `utc` counted as new; a read-back that fails ends as `withdrawn`; an *Allow* that has written
   ends through step 10 on every path, never as `kept`.
4. A withdrawal makes no claim; its stamp is one above what it saw; it reads back, looks at the lock FIRST and is
   not final while a claim is held; `lockNow` reads and changes nothing; pruning never takes the highest.
5. `regain` and every other change leave the grant's stored value alone; `mend` drops it.
6. The door's order is § P8's; `stopped` is the round's only; `waiting` is never `damaged`.
7. `readOnce` asks before every spawn; the first door alone may ask; no path spawns after the modal.
8. The row: the copy grows; only a person's *Allow* replaces a value that is not a stamp; a row that cannot be
   read stays stopped.
9. `take: 'person'` comes from `saveWaiting` and `stopAfterTorn`; `free` writes nothing.
10. No "yes" row is built; the parser still reads one.
11. The words equal § P12; the codes are literals; the census moved by the three named codes.
12. The bridge is the seven rows of § P17 and nothing else; `ACCOUNTS_READING` is untouched.
13. Every break-it R1–R33 was seen red with its message; the replaced E1 tests are named, and so is every E1 test
    whose expectation changed.
14. No doc comment says that a withdrawal "cannot" be lost or what a disk does with an exclusive create: the
    words are "the writer claims, writes and reads back" and "the withdrawal is made and read back" (V12).

## Definition of Done — E2.S1

- [ ] Gate E2-G1: this spec was reviewed by Opus before any S1 code; its B1 and F1–F7 are in this text and the
      caller checked them; X1–X16 are in the stories document and in the briefs of S2, S3 and S4.
- [ ] The caller read *Parent text to amend* and *Judgements inside the parent's text* and recorded in the
      parent's ledger what it judged a change, before any S1 code.
- [ ] E2S1-T1…T23 green; each seen RED first; each break-it R1–R33 seen red with its message, then green; all in
      the pull request, with the replaced E1 tests named and every E1 test whose expectation changed.
- [ ] Nothing is contacted without the permission, by either door, whatever is stored (T10, T11); a withdrawal
      survives a stale window, a stale lock holder and a race with an *Allow* — also one whose window ended after
      its write (T5, T6, T19, T20); it stops every side that sees its copy (T16).
- [ ] No state locks the person out without its sentence and its press (the table of § P14; T19, T21, T22).
- [ ] The two todo files T8 and T9 and their rows are deleted in the commit that turns T12 and T13 green.
- [ ] The bridge is as § P17 says, and S2, S3 and S4 each know the row they remove.
- [ ] `npm run compile` exit 0; the suite and lint green; no suppression count grew; `notification-sites.json`
      generated again, 180 sites after S1.
- [ ] E2S1-H1 green in CI's `extension · a real editor` job on the head commit, read from that job.
- [ ] `node .agents/conventions/tools/plan-lifecycle.mjs` clean.
