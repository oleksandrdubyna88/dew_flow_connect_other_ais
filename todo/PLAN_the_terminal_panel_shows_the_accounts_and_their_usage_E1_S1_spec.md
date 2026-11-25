# PLAN — E1.S1 spec: the shared store and the refusals file — a "no" that always wins, the automatic "yes", the setting (SECURITY SPEC)

> Status: **plan only, nothing implemented yet, 2026-11-19.** The security spec of story E1.S1 of
> [PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E1.md](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E1.md)
> (parent [PLAN_the_terminal_panel_shows_the_accounts_and_their_usage.md](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage.md),
> § 3 and § *Security*; its appendix, § G). Written by Fable before any S1 code (the security row of
> `subagent-models.md`: authorisation, outside the cornerstone quota). It holds the security judgement on who is
> contacted with the person's keys (§ S1.10), which must stand before S1 is coded. It keeps no change ledger: a change
> is an entry in the parent. **The parent is STOPPED at its change 1.2 (2026-11-19): no S1 code is written until the
> re-review is done and the owner has approved the next epoch.** Entry 1.1 was K5 of this spec. Entry 1.2 moves the
> person's refusals and the Stop into a file, and this spec's store design is REWRITTEN for it below: § S1.2–S1.6
> and S1.8–S1.15. Gate G2 has not seen that text. **The re-review of the stopped plan ran on 2026-11-19** (coai
> session `4082dc2c`, `proceed`, codex only): three of its five findings are about this spec's file — 0, 1 and 3 —
> and they are folded in below (*Changes to the parent*, its last paragraph, says where). They are the baseline of
> epoch 2 and no ledger entries. The owner's approval of epoch 2 is still owed. Reviewed by Opus at gate G2 on
> 2026-11-19, before entry 1.2
> ([PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E1_G2.md](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E1_G2.md));
> what stands of its verdict: a damaged list is mended with no notice and no question of its own. It is promoted
> with the E1 stories document. Parent line numbers (`:n`) are the parent's as it stood with entry 1.1, at
> `6ccf9dbe`; the revision at the stop moved its lines, so each is found there by its words.
>
> Scope: `src_vs_code` only. NEW pure `src/remoteHosts.ts`; NEW `src/remoteRefusals.ts` (the refusals file: its pure
> rules and its one writer over a disk seam); `src/chatStoreLock.ts` (its claim widened for a second file, no rule
> changed); `src/remoteRead.ts` (the road's store seam);
> `src/remoteSessionHost.ts`; `src/modelKeys.ts`; `src/sideConfig.ts`; `src/configTransfer.ts`; `src/extension.ts` (two
> arguments); `package.json` (one setting); one help sentence in five languages; the tests; two host scenarios.
> Coder: Sonnet. Review: Opus. Read at `b4aa611b` (no file under `src_vs_code/` differs from `8494bb0d`, where the
> stories document was verified). All paths are under `src_vs_code/` unless they start with `research/`, `todo/`,
> `.agents/` or are a file of the repository root. **Nothing was compiled or run for this spec:** the worktree has no
> `node_modules`. Every "RED today" below is read from today's code, not observed.

## Entry 2.1, the layout as built

The parent's entry 2.1 (the owner, 2026-11-24). Where this section and a named file, site or signature below
disagree, this section holds. Behaviour, the words W*, the notice codes and the census (186) are unchanged.

| Spec text | As built |
|---|---|
| `hostKeeper` (step 5), no file named | `src/remoteKeeper.ts`; `HostKeeper` also has `savedTorn()` (W9's third cure) and `forgetHeld()` (the test door's restore) |
| the words table moves out of `remoteHosts.ts` if it is too long | `src/remoteHostsSaid.ts`, W1–W13 |
| the `RefusalsDisk` in `remoteSessionHost.ts` (step 8) | `src/remoteRefusalsDisk.ts`; `remoteSessionHost.ts` passes `'refused.json'` and the `'unsure-'` prefix in, so both literals stay in `remoteSessionHost.ts`, once each. The scans of A1-T5 and A1-T18 for `writeFileAtomically(` and the raw write name `remoteRefusalsDisk.ts` in place of `bindRemoteSessions` |
| `remoteRefusals.ts` under 400 lines | 527 lines |
| `refusalsStore(disk, clock)` | `refusalsStore(disk, clock, sleep?)`; tests pass a macrotask yield |
| `RoadSeams` has a `now` seam | no `now` seam |
| `consentOf(` is called in `remoteHosts.ts` and in `readsKey` | `readsKey` takes the standings; `consentOf(` is called in `remoteHosts.ts` and `remoteKeeper.ts`. `consentOf` takes a structural side type, so no import cycle forms |
| `chatStoreLock.ts` take rule gives a boolean | it gives the log phrase or `undefined`, so the chat store's log line stays the same byte for byte |
| a marker whose change is in the file | removed at the next read (`sync` drops landed markers first) |

Not done here: test-first was not kept for S1 (the tests came after the code). Every break-it was then applied to
the code and seen RED, and the code was restored.

## Changes to the parent

**Two, and the second stopped the plan.**

**Entry 1.2 of the parent's ledger — 2026-11-19, `reviewer`, kinds `contract, approach`: the person's refusals and
the Stop are kept in a file under `globalStorageUri`, merged under an exclusive-create lock, and only a person's
press removes one.** The finding is the E1 plan gate's (coai session `a4a016fb`): *"S1's read-merge-write does not
make refusals atomic across windows"*. It was first rejected, with the repair of § S1.5, candidate C4 and R12 as the
reasons; residual 11 of this spec was written then. The cadence consultation `e319fe2e` showed that the rejection
does not hold: (1) if the window that gave the "no" is disposed before it repairs, a stale window's automatic write
erases the refusal for good, and the next round allows the host by itself and contacts it; (2) the Stop has the same
race with no repair at all — a stale window's `autoNoticed` write drops `stoppedUtc`. The cause was then read in this
repository's own record (F27, F28): a window sends the extension's WHOLE `globalState` object on any write. So
`globalState` cannot keep a refusal, whatever the merge and whatever the key. Why it is a change: the parent's § 3
said "One store, not two" and kept the Stop in the state row; a file with its own format, cap and lock is a contract
the parent did not name, and the repair it replaces was the parent's approach. **It is the second entry of epoch 1,
so the parent is stopped: no S1 code before the re-review and the owner's approval.**

**Entry 1.1 — K5 — is SUPERSEDED by entry 1.2.** It stays in the ledger as it was written. What it said: a *Forget*
given in another window does not lift a refusal while the window that gave it is open, because that window wrote a
missing "no" again and could not tell a *Forget* from a loss. With the refusals in a merged file no window writes a
"no" again: the repair, its timer, its notice (J2, the old W9), the sentence of the old W3, X9 and R8's run condition
are gone, and a *Forget* or a *Read* from any window of the side that keeps the refusal holds. What is new instead is
the SIDE: the file belongs to one extension host's disk (F29, § S1.5).

**What this spec said before, kept as the record.** It decided K5 "without a new row kind", and named a tombstone as
the other way: a change of kind `contract`, a second entry and so a full stop. Entry 1.2 is that stop, for a
stronger reason than a *Forget* that does not hold. It needs no tombstone: a refusal is in the file or it is not,
and only a person's press takes it out.

**One thing was fitted to the parent's text at gate G2, and it still stands.** A damaged list is mended by a
person's own answer in the change question that exists, with no notice and no question of its own (J3, § S1.3): this
spec first added the notice `remote-hosts-damaged` and the modal `mend-remote-hosts`, and both are gone. A damaged
FILE is mended the same way (§ S1.3). And nothing here moves U10 (the stories document, K3).

**The re-review (coai session `4082dc2c`, 2026-11-19, `proceed`): three findings changed this spec, and none is an
entry.** What a re-review changes is the baseline the next epoch is approved on (`plan-change-control.md`, *The full
stop*, step 4). All three were accepted by this spec's author, who is also the plan's security judge.

| Finding | What was wrong in the text of entry 1.2 | What this spec says now | Where |
|---|---|---|---|
| 0, security | A refusal the file did not take stopped only the window that took the press. The other windows of the side read on, and closing the pressing window dropped even that | The copy is written at once, before the file's write is awaited. A marker in the file's folder, made by an exclusive create that needs no lock, holds the change; while it waits, NO window of the side makes an automatic call. Any window lands it. The person is told in every window, and *Save now* is the press | § S1.4 (the order); § S1.5, *What the file did not take* and *The unsure marker*; § S1.6; W9, W12; A1-T24, A1-T26; residuals 5 and 13 |
| 1, security | The migration ran once. A "no" given afterwards in a window of an older build was a row and never a file entry | Every read of the store copies the "no" rows of the list that the file does not hold. `migratedUtc` is a record and gates nothing. The cost is named: a stale window can carry a lifted refusal back into the file | § S1.2, *The copy-in*; § S1.5, *The sides*; J-k; A1-T6, A1-T25; B9; residuals 2, 4 and 8 |
| 3, reliability | A lock 30 s old was broken. A holder that had slept past its last look at the claim then renamed a stale file over a newer refusal | No take-over by age. A lock is taken over when its owner is shown to be gone on this machine, or by a person's press. The file carries `gen` and `by`; a window that sees the file go back writes the union again | § S1.2 (the format); § S1.5, *The lock* and *A file that went back*; A1-T22, A1-T27; B32, B39–B43; residual 5 |

Findings 2 and 4 of that re-review are the round's and the script's (the stories document, S3; the S2 spec, § A2.8).

**Inside the parent's text, named so the caller can judge each.** None changes the scope, a goal, the Definition of
Done, a merge condition or the parent's row. Gate G2 judged each before entry 1.2 (its § 1.2). The re-review judges
them again with the rest; the table says what entry 1.2 did to each.

| # | What this spec does | The parent's sentence it stands on |
|---|---|---|
| J1 | **Gone with entry 1.2.** It gave the repair a third moment, once in 60 s. There is no repair and no timer of a guard | — |
| J2 | **Gone with entry 1.2.** It was the notice of a refusal written again (the old W9) | — |
| J3 | A damaged list contacts nobody, is never written over, and is mended only by a person's own answer for a host, in the change question that exists (§ S1.3). The parent does not discuss a damaged list (K6). **Fitted at gate G2:** no notice and no question of its own — a new surface was a change in doubt. **Since entry 1.2 the same rule holds for a damaged file**, and the parent's § 3 now says it | "Never overridden" (`:322`) |
| J4 | A person's answer is stamped later than the row it replaces (§ S1.4). Two windows of one profile can have two clocks (F20). Since entry 1.2 no repair compares two stamps; the rule stays so that a row's time never goes back, which the cap's order reads | "A person's "yes" beats an automatic "yes"… Only a person's own press changes a "no"" (`:336-338`) |
| J5 | The key is NOT listed in `src/settingsShape.ts` (§ S1.7, K9). The parent lists that file (`:387`). A key there must reach the server's settings file (F14) and is read through a per-side overlay (F13) | "Only the person's own user-level value counts" (`:382`) |
| J6 | At a full list a person's answer may push out the oldest person's "yes", never a "no" (§ S1.4, K7). The parent speaks only of the automatic row | "The cap protects the person's answers" (`:348`) |

## Changes to § 4 and § 5 of the stories document (for gate G2)

The caller puts these into the stories document and into the S3 and S4 briefs in ONE commit, before S3 or S4 is
coded. The rows are X1…X22; X14…X16 were added at gate G2, from its findings C-2, C-3 with S-2, and its § 4. **They
are in the stories document since 2026-11-19** (its § 4 and its S3 and S4 briefs). **X17…X20 were added at the stop
(the parent's entry 1.2), and X2, X6, X9, X10, X13 and X15 were rewritten for it**; the stories document holds them
since the same day. **X21 and X22 were added at the re-review (session `4082dc2c`), and X2, X9, X18 and X19 were
changed for it.**

| # | Where | The stories document says | This spec says | Who must follow |
|---|---|---|---|---|
| X1 | C1, the file | The pure half is in `src/remoteRead.ts` | The store is in NEW pure `src/remoteHosts.ts` (the `remote` prefix, so the log scan covers it). `remoteRead.ts` is 509 lines today and would pass 800 | S3, S4 import from `./remoteHosts` |
| X2 | C1, `HostChange` | Three kinds: `person`, `forget`, `auto` | One more, sent only by S1 itself: `mend` (a person's answer for one host over a damaged list or a damaged file, which repairs it in the same press). **`repair` is gone (entry 1.2).** A person's "no" is written to the list as its copy AT ONCE and then to the refusals file (the re-review's finding 0; entry 1.2 had the file first); a *Read* or a *Forget* is written to the file first. The callers still send one change and get one answer, which is the FILE's | S3 and S4 do not send `mend` |
| X3 | C1, `Committed` | Named, not defined | § S1.4: `written`, `kept`, `full`, `damaged`, and — since entry 1.2 — `unwritten` (the refusals file could not be written; § S1.5) | S3 (the state row uses its own result type) |
| X4 | C1, `consentOf` | "stays the one function" | `consentOf(file, value, host, allowNew)`: it is given the refusals file as it was just read and the list; a "no" in either is `no`. `Consent` gains `'damaged'` | — |
| X5 | § 2, the store seam | `RoadSeams.store` `{ read, write(rows) }`; `AnswerStore`; `RemoteSessions.answers` | `RoadSeams.store` is `{ consent(host), answer(host, answer) }`. `withAnswer`, `withoutHost`, `answersOf`, `AnswerStore.write` and `RemoteSessions.answers` are gone. No function of the product takes a list to write | S3's change-signal listener reads `hostStandings()` |
| X6 | C1 and C5, the commit primitive | "reusable for another `globalState` key" | The pure `commit(state, key, change, merge)` is exported. S3 writes the state row through the BOUND `sessions.commitKey(key, change, merge)`, never through the pure one. **Since entry 1.2 the bound one runs no repair: there is none.** It stays the one door so that every `globalState` write of this feature is the commit of § S1.4 | S3 |
| X7 | C1, for the list | `ignoredLayerOf(key)` (pure, in `modelKeys.ts`) | Pure `ignoredLayerOf(inspect, key)` in `modelKeys.ts`; bound `sessions.ignoredLayer()` and `sessions.allowsNewHosts()` for S4's view | S4 |
| X8 | C1, `hostStandings()` | each row as the list shows it | Unchanged. Over a damaged list it gives the rows that can be read. S4 learns "damaged", "full" and "not asked yet" from `HostMemory.admission`, which S3's round sets from each admission that was not `read` (the stories document, C4; gate G2, C-4), and draws W11 | S3, S4 |
| X9 | A1-T6 | "a person's *Read* or *Forget* of that host in another window is not undone" | **Rewritten at entry 1.2:** neither is undone by the window that gave the "no": no window writes a "no" row again. (Before: a *Forget* in another window WAS undone — K5, the parent's entry 1.1, superseded.) **Since the re-review's finding 1 one case undoes a lift:** a window whose view has not caught up reads inside the lag and copies the old "no" row into the file (residual 4; A1-T6 (b2), (c2)). A1-T14 gains a fourth description (an automatic row with the setting off) | The reviewer |
| X10 | C7 and A3-H1 | A3-H1 removes its rows "by S1's commit (*Forget*)" | The road's test door has `commit`, `admit`, `standings`, `stored` and `restore`; `answers` is gone. A3-H1 restores with `restore(stored())` taken before it started. **Since entry 1.2 `stored()` is the list AND the text of the refusals file, and `restore` puts both back** | S3 |
| X11 | A1-H1 | one host scenario | A1-H2 added: the setting through the real configuration | — |
| X12 | C6 | — | `dayOf(askedUtc)` is exported from `remoteHosts.ts`; the hosts list (S1) and the host sentence *allowed automatically on <day>* (S4) both call it | S4 |
| X13 | § 3, the census | 185 sites | S1 adds ONE, so 186 after S1. **Since entry 1.2 that one is `remote-host-refusal-not-kept` (W9): a "no" or a Stop that the refusals file did not take.** `remote-host-refusal-restored` does not exist any more. The two others this spec first added, `remote-hosts-damaged` and `mend-remote-hosts`, do not exist (J3 fitted at gate G2). S4 counts from what S1 leaves | S4 |
| X14 | S3, `armFor` (gate G2, C-2) | "an eligible host = … a host whose `consentOf` is not `no`" | S3 never calls `consentOf`. A host is eligible for the timer when `sessions.hostStandings()` holds no "no" for it. Whether a host is CONTACTED is `admitHost`'s answer alone, asked before every call | S3; A1-T2's scan stays as written (`consentOf(` in `remoteHosts.ts` and in `readsKey` only) |
| X15 | C5, the state row (gate G2, C-3 and S-2) | `accountsStateOf` reads a damaged value as `{}`; `withStateChange(state, change)` takes a parsed state | S3's merge for the state row is a `Merge<C, R>` of § S1.4 — over the stored value as `unknown` — given to `sessions.commitKey`. A damaged state row reads as STOPPED, and only a person's *start* or *stop* writes over it: the rule of J-d for this row. **Since entry 1.2 `stoppedUtc` of that row is the COPY of the Stop; the Stop itself is the file's (X17)** | S3; A3-T23 |
| X16 | C7, the open point below (gate G2, § 4) | — | In `ExtensionMode.Test` the round never arms itself, and the default accounts call is a stub that spawns nothing until a scenario replaces it | S3; A3-T27 |
| X17 | C5 and S3, the Stop (entry 1.2) | The Stop is `stoppedUtc` of the state row, written by `stateMerge` | The Stop lives in the refusals file. S1 exports the bound `sessions.stopRound()`, `sessions.startRound()` — each answers `Committed` — and `sessions.roundStop(): Promise<'stopped' \| 'running' \| 'damaged'>`, read from the disk. S3's stop is: `stopRound()` first, then the copy `{ stop: utc }` through `commitKey`, whatever the first answered. S3's start is: `startRound()` first; only on `written` or `kept` the copy `{ start: true }`. S3's `stopped` seam is true when `roundStop()` is `stopped` OR `stoppedOf` of the state row is true | S3; A3-T23 |
| X18 | S3, the round's door (entry 1.2) | `admitHost` "runs the refusal guard" | `admitHost` runs no guard. It reads the refusals file from the disk, then the list and the setting, before EVERY call (§ S1.6). `Admission` gains `{ kind: 'unwritten' }`: a "no" or a Stop is not in the file yet — this window's own, or one that a marker of the side holds (the re-review's finding 0) — so this window contacts no host. S3 keeps it in `HostMemory.admission` like every admission that is not `read` | S3, S4 (the sentence W12) |
| X19 | S4, the rows (entry 1.2) | A host's row over a damaged list: W11 | The same row and the same press over a damaged FILE (W11 says which of the two cannot be read). The stopped list, when a start could not be written or the file is damaged: W13. The row W12 has a press since the re-review (X21) | S4 |
| X20 | § 2 and C7, activation (entry 1.2) | `bindRemoteSessions(context)` over the `Memento` | It is also given the folder of the refusals file, `vscode.Uri.joinPath(context.globalStorageUri, 'terminal-hosts').fsPath`; `testDoorOf` gets the same folder as a third argument. No other module names that folder | S3 (A3-T24 lists the file among what is stored) |
| X21 | S4, the row W12 and the notice W9 (the re-review, findings 0 and 3) | The row W12 says that this window holds an unsaved refusal, and has no press | S1 exports the bound `sessions.saveWaiting(): Promise<Committed>` — a person's press ONLY: it takes the lock over whatever its owner, lands every change that waits on this side, and removes a marker that cannot be read, leaving the round stopped then. It is called from two places: the action *Save now* of W9 (S1's own notice) and a press on a row that shows W12 (S4). The round and the timer never call it (A1-T18 scans) | S4 (the row's press); S3 does not call it |
| X22 | S3, A3-T24 (the re-review, findings 0 and 3) | The fake disk holds at most `refused.json`, with no key but `v`, `migratedUtc`, `refused` and `stop` | The file's keys are `v`, `gen`, `by`, `migratedUtc`, `refused` and `stop`; the folder may also hold `unsure-<token>.json` markers, each with no key but `v`, `kind`, `host` and `utc`. No value in either came from a reply | S3 |

**The open point for S3, found here, was decided at gate G2 (its § 4; X16).** Three existing host scenarios open a
terminal called `Creds: h / s` (`src/test/host/terminalHostsScenario.ts:56`, `src/test/host/terminalDoorsScenario.ts:60`,
`:94`). After S3, a round armed by such a terminal would start the REAL ssh 30 s later, in CI's real-editor job. So in
test mode the round is never armed by itself, AND an accounts call that a scenario has not replaced is a stub that
never reaches `ssh`: two locks, both in S3's brief (the stories document, S3, and A3-T27).

## S1.0 — What was read

| # | Fact | Where |
|---|---|---|
| F1 | The row today: `{ host, answer: 'yes' \| 'no', askedUtc }`; `Consent` is `yes`, `no` or `ask`; the cap is 100 | `src/remoteRead.ts:29-33`, `:35`, `:38` |
| F2 | A row is valid with a non-empty `host`, a `yes` or `no`, and ANY string as `askedUtc`. Other keys are ignored | `src/remoteRead.ts:114-124` |
| F3 | A value that is not an array of valid rows reads as EMPTY — one bad element empties the list | `src/remoteRead.ts:127` |
| F4 | `consentOf`: a "no" wins over another row of the host; no row is `ask` | `src/remoteRead.ts:130-137` |
| F5 | `withAnswer` replaces the host's row and, at the cap, drops the OLDEST row by a string compare of `askedUtc` — a "no" included. The other rows are kept by reference, so their extra keys survive | `src/remoteRead.ts:139`, `:142-147` |
| F6 | `readsKey`, `answersOf`, `withoutHost` | `src/remoteRead.ts:154-158`, `:163`, `:166` |
| F7 | The road's store seam takes a WHOLE list. Both writers of today read the store in the same expression that writes it — after the `await` of the person's answer, not before it. The consent is read once, at the start of a press; a later leg re-checks only the terminal's name | `src/remoteRead.ts:323-325`; `:356-364` (`:361`); `src/remoteSessionHost.ts:136-141` (`:139`); `src/remoteRead.ts:366-370`, `:380-387`, `:458-470` |
| F8 | The host half: the key; the question; the store over the `Memento`; the three choices; the list row; the empty notice; the change question; the context key; the binding; the test door | `src/remoteSessionHost.ts:23`; `:35-45`; `:75-78`, `:161-167`; `:94-98`; `:104-105`; `:107-114`; `:124-133`; `:207-210`; `:213-225`; `:231-247` |
| F9 | The tests that hold today's rules: a damaged list is `ask` (S3-T1); the oldest row goes, a "no" among them (S3-T2); *Forget* (S3-T2b); no ssh before a "yes" (S3-T3); the fake store; the context key | `src/test/remoteRead.test.ts:153-168`; `:170-183`; `:185-196`; `:198-230`; `:107-121`; `:498-513` |
| F10 | Two host scenarios set and restore the store by whole lists | `src/test/host/terminalHostsScenario.ts:55`, `:59`, `:67`, `:87`; `src/test/host/terminalDoorsScenario.ts:59`, `:63`, `:73`, `:75`, `:78`, `:87`, `:96`, `:112` |
| F11 | The test door is built only in test mode; a test holds that | `src/extension.ts:104-122`, `:161`, `:933-938`; `src/test/remoteExportGuard.test.ts:16-27` |
| F12 | The user-layer door: `SAFETY_KEYS` (one key today) and its doc; `USER_LAYER_KEYS`; the reader; the user layer's value is `globalValue`, else the default; the layer finder is private and walks the model keys only | `src/modelKeys.ts:26-37`; `:52`; `:78-88`; `:107-109`; `:119-136` |
| F13 | The door's host half reads the user layer; `readerFor` puts a per-side overlay in FRONT of it, and that overlay answers for ANY key it holds; `kimiAllowNetworkSignIn` is kept per side | `src/sideConfig.ts:47-49`; `:29-39`; `src/settingsShape.ts:394-397`; `:317-345` (`:337`); `src/sideSettings.ts:66-73` |
| F14 | A key of `CoaiSettings` is read by `settingsFrom` and must change the server's env block, unless it is in a list of one | `src/settingsShape.ts:249`, `:419-434`; `src/test/settingsReach.test.ts:102`, `:104-116` |
| F15 | The three `application` keys of today; none of their names is in `src/settingsShape.ts`. The Kimi key is `machine` | `package.json:418-421`, `:445-448`, `:643-656`; `:669-672` |
| F16 | A key in `NEVER_TRANSFERRED` is not exported, and an import refuses it by name with its reason | `src/configTransfer.ts:29-55`; `:92-109`; `:115-117`; `:179-193`, `:256-263` |
| F17 | The tests of the door: the safety list is held exact; the scan for a direct read is BUILT from `USER_LAYER_KEYS`; the files that are the door | `src/test/modelKeys.test.ts:67-73`; `src/test/modelKeysAreReadOnce.test.ts:32-35`, `:42`, `:57-68` |
| F18 | Every setting of the manifest needs a `SETTING_ALIAS` phrase that the English help holds | `src/test/helpCoverage.test.ts:451`, `:557-563` |
| F19 | A notice has `title`, `detail`, `cure`, `action` or `actions`, `modal`, `subject`; the two doors used here; the census constant | `src/notice.ts:24-71`; `src/notify.ts:247-252`, `:351-360`; `src/test/notificationSites.test.mjs:290` |
| F20 | `globalState` is one store for every window of a profile, a Windows window and a WSL window among them, and the extension runs in the WSL extension host for a WSL window. A write by one window was seen by the other, already open, at its next read — at most 23 s and 20 s later; the lag itself was not measured. "One key per side… so two windows never read-modify-write one value" | `research/RESULTS_side_registry_reaches_the_other_window.md:10`, `:20`, `:34-35`, `:42`; `src/sidesSeen.ts:10-13`, `:15-16` |
| F21 | A store seam that a `Memento` fits, free of `vscode` | `src/sideSettings.ts:11-14` |
| F22 | A host is `[A-Za-z0-9][A-Za-z0-9._-]*`, from the terminal's name | `src/credsSession.ts:26-36`, `:65-69` |
| F23 | The log scan reads the arguments of every `notify(` call of a `src/remote*.ts` file for a list of words (`rows`, `names`, `reason`, `folder` among them) | `src/test/remoteLogScan.test.ts:16`, `:43-50`, `:71-79` |
| F24 | `SECURITY.md` says today: "No ssh child is started before a "yes"" | `SECURITY.md:126-146` (`:130`) |
| F25 | No source file calls `setKeysForSync` (a search of `src/` finds the word in two comments only) | `src/sidesSeen.ts:13`, `src/cliPathKeep.ts:18` |
| F26 | A test pins where `exitStatus` is read in `remoteSessionHost.ts`; the coder keeps `liveNameOf` as it is | `src/test/terminalReattachWiring.test.ts:22-27` |
| F27 | **Added at the stop (entry 1.2).** `Memento.update` changes the window's own copy at once and schedules ONE send of the extension's WHOLE `globalState` object; when the client's value changes, every window REPLACES its whole copy, and a write a window had not sent yet is dropped with no error. So a write built on a stale copy puts back every key that copy held, and `await update()` proves nothing. Read in VS Code's source (`extHostMemento.ts`, `mainThreadStorage.ts`) on 2026-11-17 for another plan here, verified by its coordinator and read again by its judge; not pinned to the editor a person runs, and NOT read again for this spec | `research/PLAN_auto_never_asks_a_person_E2_S2_spec.md:119-121` (V1–V3), `:112-115`; `research/PLAN_auto_never_asks_a_person.md:683-686` |
| F28 | The lag between two windows is not measured: 20 s and 23 s are upper bounds, from one run on one machine | `research/RESULTS_side_registry_reaches_the_other_window.md:27-31`, `:34-36` |
| F29 | `globalState` is the CLIENT's storage, shared by every window of a profile, remote windows included; `globalStorageUri` is a path on the extension host that is RUNNING — the Windows profile folder in a local window, `~/.vscode-server/…` in a WSL one. One record, two disks | `src/coaiInstall.ts:303-309`; `src/installer.ts:47-49` |
| F30 | The lock this repository has: an exclusive create (`'wx'`) of `<id>.lock` with a note `{ pid, at, token, doing }`; a lock older than 30 s may be broken, after a re-read that it is still the lock that was aged; a held lock is waited on for about 120 ms (6 tries) and then reported `held`; release removes the lock only when its note reads back with this claim's token. Its header states its residual: two breakers of one stale lock can both hold it, and the last rename wins. The id must pass `isSafeId`. Its sentences say "conversation" | `src/chatStoreLock.ts:18-22`, `:47-73`, `:80`, `:83-84`, `:87-93`, `:209-228`, `:240-264`, `:286-316`; `src/chatStore.ts:205-206` |
| F31 | The atomic write this repository has: written beside the destination under a name of the pid and a counter, then renamed over it with a retry for transient errors; the beside-file is removed when the write fails; the folder is made if it is missing | `src/atomicFile.ts:29`, `:58-60`, `:103-116`, `:126-152` |

**UNVERIFIED — not read in this repository, not observed.** Each is named again where the design leans on it.

| # | What | What the design does about it |
|---|---|---|
| V1 | **Answered at the stop (entry 1.2) by this repository's own record, F27:** one `Memento.update` sends the extension's WHOLE state as that window holds it. It was not observed in an editor from this checkout | The refusals and the Stop are not kept in `globalState` (§ S1.5). A1-T3, A1-T19 and A1-T20 run against the `'whole-state'` fake |
| V2 | How soon a window sees another window's write (the parent's U12). Only an upper bound is measured (F28) | It no longer decides whether a refusal holds on the side that keeps it. It bounds the residuals 2–4 of § S1.10; R12 measures it |
| V9 | That an exclusive create and a rename over an existing file behave on the person's disk as F30 and F31 say — a network share or a synced folder as the profile folder was not tried | The writer reads the file back after every write and does not report a change it cannot see (§ S1.5, step 6) |
| V10 | What a WSL or a remote window's `globalStorageUri` is on the owner's machines. Two WSL distros with the same user name have the same PATH there and two disks (`src/coaiInstall.ts:313-316`) | Each extension host's disk has its own file, whatever its path. R12 (d) records which sides the owner has |
| V11 | **Added at the re-review (finding 3).** That `process.kill(pid, 0)` throws `ESRCH` for a process that is gone and for no process that lives, on Windows as on Linux (Node's documentation says so; not run here). That `/proc/sys/kernel/random/boot_id` is there in a WSL distro and on a remote Linux host and changes with every restart. That Windows and macOS give no exact boot id without starting a program. That two extension hosts never see ONE storage folder from two process tables (a folder shared with a container of the same host name) | Only `ESRCH` or another boot id, on a lock whose note names this machine, is "gone"; every other answer is "not gone" and waits for a person (§ S1.5). Where no boot id can be read that half of the proof is not used. A wrong "not gone" fails closed. A wrong "gone" makes two writers, which the read-back and *A file that went back* answer (residual 5c). A1-T27 runs the rule over a seam; nothing here observes a real machine |
| V3 | What `inspect()` answers for an `application` key that a workspace file sets; what a WSL or remote window reads for such a key; whether Settings Sync carries it | The read never looks at a workspace or folder value, whatever VS Code hands over. R11 and A1-H2 observe |
| V4 | That another extension can write this extension's user-level setting | Named as a residual (§ S1.10) |
| V5 | That a host scenario may write a user-level, `application` key in CI's editor | A1-H2 fails, it does not skip, if it may not |
| V6 | How a modal draws a `detail` of several sentences | The words are sentences, with no layout in them |
| V7 | That a WSL window's clock and the Windows clock can differ | J4 does not need them equal |
| V8 | That a workspace's task can give a terminal a chosen name (the appendix says so, `:161-162`) | Judged as true in § S1.10 |

## S1.1 — Decisions

1. **One module holds the store, and it exports no list to write.** `src/remoteHosts.ts`, pure. What it exports reads
   a value, commits a CHANGE, or says words. The function that turns a list and a change into the next list is
   private to it.
2. **One function says whether a host may be contacted: `consentOf(file, value, host, allowNew)`.** The round's door
   and the session road both ask it, and nothing else decides. It answers `yes`, `no`, `ask` or `damaged`. A host is
   contacted only on `yes`.
3. **One function writes the list: `commit`.** It reads the stored value and calls the write in the same step, with
   no `await` between. It takes a change for one host. Today's two writers already read at the write (F7); what they
   lack is the merge, a cap that keeps a "no", and a seam that cannot be handed a list. (It makes a stale VARIABLE
   harmless. It cannot make a stale WINDOW harmless, F27; decision 8 is the measure for that.)
4. **The merge, for one host:** a person's "no" beats everything. A person's "yes" beats an automatic "yes". An
   automatic "yes" is written only where the list read in that step has NO row for the host. Only a person's press
   changes a "no". (The parent's rule 2, `:336-338`, as a table in § S1.4.)
5. **A list that cannot be read is not an empty list (K6).** `undefined` — the key was never written — is empty.
   Anything else that is not an array of valid rows is `damaged`: no host is allowed by itself, no host at all is
   contacted by the round, and no commit writes over it. Only a person's press mends it: an answer for a host, given
   in the change question that exists. A damaged list has no notice and no question of its own (gate G2). (This spec
   says "mend" for a damaged list and "repair" for a refusal that is written again, so the two are never mixed up.)
6. **A row with a marker this build does not know is not a row.** `how` is absent or `'auto'`. Any other value, and
   an `'auto'` beside a "no", make the list `damaged`. Such a row could mean anything a later build decided; read as
   a person's "yes" it would contact a host on an unknown ground, and read as automatic it could be pushed out.
7. **The cap never drops a "no", and an automatic row never pushes out a person's row (K7).** § S1.4 has the order.
8. **A refusal and the Stop are kept in a file of the side, not in `globalState` (the parent's entry 1.2; § S1.5).**
   One function writes the file, under an exclusive-create lock, and it takes a change. Every decision reads the file
   from the disk. Only a person's press removes a refusal or the Stop. No window writes a "no" ROW again: the guard
   of the first text, and K5 with it, are gone.
9. **A person's answer is stamped later than the row it replaces.** So a row's time never goes back, whatever two
   clocks say.
16. **A "no" in either place is a "no".** The file is the authority of its side; the "no" row of the list and
    `stoppedUtc` of the state row are copies, written for the other sides of the machine and for an older build. A
    copy is never needed for a refusal to hold on the side that keeps it, and a copy alone is still honoured.
17. **What stays in `globalState` is what is safe to lose** (§ S1.5, the table): a person's "yes", an automatic
    "yes", the two notice times. Losing one leads to a new automatic allow, a new question or a repeated notice.
18. **A file that cannot be read contacts nobody; a file that cannot be written never reports a refusal as kept.**
    In the second case the window that took the press treats the refusal as in force and contacts no host until the
    write lands — and, since the re-review, so does every other window of the side (decision 21).
19. **The lock is the one this repository has** (`src/chatStoreLock.ts`), widened and not copied. Its stated
    residual is answered by a read-back after every write, and — since the re-review — its take-over by age is NOT
    used for this file (decision 23).
20. **Nothing automatic shortens the file.** The copy-in, the writing back of a file that went back and the mend
    only add; the round, the cap and a reload never write a removal. A test holds it by construction (A1-T21).
21. **A refusal or a Stop that the file did not take is known to the whole side (the re-review's finding 0).** Its
    copy is in `globalState` before the file's write is awaited. Its marker is a file of its own in the same
    folder, made by an exclusive create, which a held lock cannot stop. While a marker's change is not in the
    file, no window of the side contacts a host or allows one by itself, and every window tries to land it.
22. **The "no" rows of the list are copied into the file at EVERY read, not once (finding 1).** A "no" in either
    place counts, and the file only gains by it. A copy is still never needed for a refusal to hold on the side
    that keeps it (decision 16).
23. **No lock of this file is taken over by its age (finding 3).** A held lock is taken over when its owner is
    shown to be gone on this machine, or by a person's press, and by nothing else. The file carries `gen` and `by`;
    a writer looks at both after its rename, and every window writes the union back when the file went back.
10. **The setting is read from the user layer, afresh, at every call**, through the door that exists, and through
    no per-side overlay. It is on when the user layer holds `true` or nothing. Any other value there is OFF, the safe
    side.
11. **The setting joins `SAFETY_KEYS`**, as the parent says, and the list's doc is widened to say what it now holds.
    It does not join `CoaiSettings` (K9, § S1.7).
12. **The door of the round never asks.** The keeper's seams have no ask. The session road keeps its question.
13. **The session road is not serialized with the store beyond its first read**, as today: a "no" given while a
    press runs does not stop that press's later calls. Named (§ S1.10), not changed: the parent says the session
    reader behaves as before (`:887`).
14. **No whole-list write in the product, and one in the test door (K15).** Scenarios set a host's answer by a
    commit. They put the profile back with `restore`, which exists only in test mode.
15. **The words are in the pure module**, so a unit test holds them (A1-T14). The host file only passes them on.

## S1.2 — The row, old and new, the refusals file, and the migration

```ts
/** One host's row, as it is kept in `globalState` under `coai.terminalHosts`. */
export interface HostAnswer {
  readonly host: string;            // not empty; compared exactly
  readonly answer: 'yes' | 'no';
  readonly askedUtc: string;        // UTC ISO; for an automatic row, the instant the round wrote it
  readonly how?: 'auto';            // NEW. Only on a "yes" the round's door wrote. A person's row has no `how`
}
```

| | Old (0.87.0 … 0.89.1) | New |
|---|---|---|
| The value | absent, or an array of at most 100 rows | the same |
| A row | `{ host, answer, askedUtc }` | the same, or with `how: 'auto'` on an automatic "yes" |
| Who writes a row | a person's press | a person's press; the round's door (automatic) |
| What a "yes" row allows | one session read, on a press | parent § 3 (`:358-361`) |
| What a "no" row is | the refusal | the COPY of a refusal; the refusal is in the file of the side (below) |

**The rows: no migration.** Every row stored today has no `how`, so it reads as a person's answer (parent `:324`,
appendix `:573-575`). No row is rewritten at activation. The first commit keeps every other row by reference, as
`withAnswer` does today (F5), so a key a later build adds to a row survives this build.

**The refusals file (the parent's entry 1.2).** One for each side of the machine (F29), in a folder of its own, so
that no sweep of another store sees it:

```
<globalStorageUri>/terminal-hosts/refused.json      the file
<globalStorageUri>/terminal-hosts/refused.lock      the claim (F30), present only while a write runs
<globalStorageUri>/terminal-hosts/refused.json.<pid>.<n>.tmp   the beside-file of a write (F31)
<globalStorageUri>/terminal-hosts/unsure-<token>.json   a marker: one refusal or Stop the file did not take (§ S1.5)
```

```ts
/** The refusals file of one side, version 1. Every time is UTC ISO. Other keys are kept through a write and not read. */
export interface RefusalsFile {
  readonly v: 1;
  /** A whole number, 1 or more, one higher with every write. The file that was never written counts as 0. */
  readonly gen: number;
  /** The token of the write that made this file. Two files of one `gen` and two `by` are two writers on one baseline. */
  readonly by: string;
  /** Set by the FIRST copy-in: the "no" rows of `coai.terminalHosts` were first copied in at that instant. A record; it gates nothing. */
  readonly migratedUtc?: string;
  /** At most `HOSTS_MAX` (100). One entry for a host; `utc` is when the person refused it. */
  readonly refused: readonly { readonly host: string; readonly utc: string }[];
  /** Present while the accounts round is stopped. */
  readonly stop?: { readonly utc: string };
}

/** The file may not be larger than this, read or written. A full file is about 8 KB with hosts of 30 characters. */
export const REFUSALS_BYTES_MAX = 64 * 1024;
```

| What is on the disk | Reads as |
|---|---|
| no file (`ENOENT`) | the empty file, `gen` 0, no refusal and no Stop — the file was never written, or was deleted (§ S1.10, residual 7) |
| JSON, an object, `v` is `1`, `gen` a whole number of 1 or more, `by` a non-empty string, `refused` an array of `{ host: non-empty string, utc: string }`, `stop` absent or `{ utc: string }`, `migratedUtc` absent or a string, at most 64 KiB | that file. Two entries of one host count as one, and the next write leaves one |
| anything else: not JSON; not an object; `v` missing or not `1`; `gen` or `by` missing; a key of the wrong shape; more than 100 refusals; more than 64 KiB; a read that fails for another reason than `ENOENT`, three times, 20 ms apart | **damaged** (§ S1.3) |

A `v` this build does not know is damage, not an old file: a later build decided something this one cannot read, and
reading it as empty would allow every host (decision 6, for a file).

**The copy-in, at EVERY read (the re-review's finding 1; the text of entry 1.2 had "the migration, once for each
side").** At every decision of this build on a side — `admit`, `consent`, `standings`, `roundStop` and every
`commit` of a host's change; not `commitKey`, which stays the plain commit of A1-T16 — when the list can be read:
every "no" row of `coai.terminalHosts` whose host the file does not refuse is added to
`refused`, with its `askedUtc` as `utc` (`now` when that is no time), up to the cap, under the claim and in ONE
write for all of them. The first such write sets `migratedUtc`; a later one leaves it. Where no row is missing
nothing is written, so a read with nothing to copy costs no write. Nothing is removed from `globalState`. When the
list is damaged the copy-in waits: the damaged list already contacts nobody. A copy-in that the file does not take
is a refusal the file did not take: the rows count as copies (decision 16), and the hosts are held and marked like
a person's "no" (§ S1.5, *What the file did not take*), with the same notice.

**Why at every read, and what it costs.** The text of entry 1.2 copied once and then honoured a later "no" row as a
copy only, so that a stale window could not bring back a refusal a person had just lifted. The re-review showed the
worse error on the other side: a "no" given in a window of an older build AFTER the migration was never in any
file, and a copy can be erased for good by a stale write (F27). A refusal that is lost is worse than a lift that
has to be pressed twice. So the copy-in runs at every read, and the cost is named: after a *Read* or a *Forget*,
a window whose view of `globalState` lags still holds the old "no" row (F28), and if it makes a decision in that
time it copies the row back into the file. The host is then refused again, on that side, until the person presses
once more (§ S1.10, residual 4). The cure is a record of what was lifted — what is left of the parent's candidate
C4b — and it is not built here. The keeper runs its own operations one at a time (§ S1.5), so a window's own lift
is never undone by a decision of the same window that ran in the middle of it.

**An older build beside this one, or after a downgrade (point 6 of the stories document).** A window that was open
before the update keeps the old code until it is reloaded. That window reads an automatic row as a person's "yes"
(F2), so its session door reads without asking — also when the setting is off, which it does not know. It keeps
`how` on the rows it does not touch (F5). At its cap it drops the oldest row, a "no" among them (F5) — a copy now,
since the copy-in put that refusal into the file. It does not read the file: a host refused only there is read by
ITS session door on a press. A "no" given in it is a row and no file entry — until a window of THIS build on that
side reads the store, which copies it in. All of this is a residual until the old window is reloaded (§ S1.10,
residual 8). Nothing in this build depends on an old build keeping anything.

## S1.3 — Reading the store, and a list or a file that cannot be read (K6)

One private parser, `hostsOf(value)`, and nothing else reads the stored value.

| The stored value | Reads as |
|---|---|
| `undefined` (the key was never written) | no rows |
| `[]` | no rows |
| an array whose every element is a valid row | those rows |
| an array with at least one element that is not a valid row | **damaged**; the valid elements are its readable rows |
| anything else (`null`, a string, a number, an object) | **damaged**, with no readable row |

A valid row: `host` a non-empty string; `answer` `yes` or `no`; `askedUtc` a string; `how` absent, or `'auto'` beside
a `yes`. Other keys are ignored and kept. Two rows of one host are not damage: the strongest counts — a "no", then a
person's "yes", then an automatic "yes" — and the next write for that host leaves one.

```ts
export type Consent = 'yes' | 'no' | 'ask' | 'damaged';

/**
 * The side as it was just read from the disk: the file with what waits beside it (the markers of § S1.5), or the
 * fact that the file or its folder cannot be read. `Waiting` is § S1.5's.
 */
export type RefusalsRead =
  | { readonly kind: 'file'; readonly file: RefusalsFile; readonly waiting: readonly Waiting[] }
  | { readonly kind: 'damaged' };

/** Whether this host may be contacted. The ONE function that says so. A host is contacted only on 'yes'. */
export function consentOf(file: RefusalsRead, value: unknown, host: string, allowNew: boolean): Consent;
```

In this order: (1) the file holds a refusal for the host, or the list holds a readable "no" for it → `no`, also when
the other of the two is damaged (decision 16); (2) the file is damaged, or the list is damaged → `damaged`, also for
a host whose readable row is a "yes" — what cannot be read may be that host's "no"; (3) a person's
"yes" → `yes`; (4) an automatic "yes" → `yes` when `allowNew`, else `ask` (parent `:377-379`: with the setting off an
automatic row counts as no answer, and it is kept); (5) no row → `ask`. `consentOf` is pure: the caller reads the
file (§ S1.5, *A read*) and hands it in. Whether the ROUND is stopped is not its question; § S1.6 asks that first.

**What a damaged FILE does (the parent's entry 1.2).** Everything a damaged list does, below, with these
differences. A file is read whole or not at all: it has no readable part. What can still be read is the list, so a
host whose row is a "no" answers `no` and says *turned off*; every other host answers `damaged`. The mend is the same
press — a person's answer for ONE host in the change question that exists (W7 says which of the two cannot be read)
— and it writes a NEW file in one write under the claim: `refused` is every readable "no" row of the list, the
refusals of the newest file this window remembers (§ S1.5, *A file that went back*), those of the markers that can
be read, then the person's answer when it is a "no"; `gen` is one higher than the highest this window remembers;
`migratedUtc` is `now`; and **`stop` is set to `now`**. A damaged file may have
held the Stop, and the Stop is the way out when a read does harm on a VM (parent § 3), so a mend never starts the
round: the list then shows the stopped row, and the person starts the round by a press of their own (W13). A
refusal that was kept only in the damaged file, and that no open window remembers, is lost; W7b says so before the
person answers. No operation but a
person's `mend` writes over a damaged file: not the copy-in, not a "no", not a Stop, not a start. A person's "no"
or Stop over a damaged file is therefore a `mend` when it comes from the change question, and `unwritten` — with the
window treating it as in force — when it comes from anywhere else (§ S1.5). While the file is damaged the round
makes no call, so nothing is lost by the Stop not being writable.

**What a damaged list does, everywhere.**

- **The round's door** answers `refused` for a host with a readable "no" and `damaged` for every other host. No ssh
  call, no automatic row.
- **Every commit** but a person's `mend` answers `damaged` and writes nothing. The stored value stays the same
  value. So a row a later build wrote is never destroyed by this build.
- **The session road** does not ask and does not read. It answers W6 (§ S1.8). Today it asks, and the answer
  overwrites the whole value with one row (F3, `src/remoteRead.ts:361`).
- **The terminal's two overflow items stay shown** (`readsKey` is true unless the answer is `no`): the press says
  what is wrong.
- **The person is told** in three places, each of which exists or is a row of a list — no notice and no question of
  its own (J3, fitted at gate G2): the session door's sentence (W6); the host's row of the accounts list (W11, drawn
  by S4); and *CoAI: hosts read over ssh*, whose change question then says what is damaged (W7). Until the person
  looks at one of the three, nothing tells them: the cost is in § S1.10, residual 6.
- **The mend is a person's answer for ONE host, in the change question that exists** — code
  `change-remote-host-answer`, its three buttons *Read*, *Not this host* and *Forget*; no new code, no new button, no
  new notice site. Over a damaged list the question's detail starts with W7: how many answers can be read, how many
  of them are refusals, how many entries cannot be read, that one of those may have been a refusal, and that an
  answer given here repairs the list. *Read*, *Not this host* or *Forget* then commits ONE change,
  `{ kind: 'mend', host, answer }`: the readable rows, one for each host, at most 100 with the refusals first, and —
  in the same write — the person's answer for that host, by the merge and the cap of § S1.4. Escape changes nothing.
  So the mend is what the parent wants of every change to a "no": a person's own press (parent `:338`).
- **How a person reaches that question.** `changeHost(host)` opens it: from *CoAI: hosts read over ssh*, and from a
  host's row of the accounts list (S4). `askHost(host)` over a damaged list is `changeHost(host)`. Over a damaged
  list *CoAI: hosts read over ssh* lists the hosts whose answer can be read AND the hosts of this window's live
  `Creds:` terminals that have none (the description W4c), so a list in which no answer can be read is still mended
  from there. The rows are chosen by a pure function of this module, `listedHosts(value, liveHosts)`; the host file
  hands it the hosts of `liveTerminals`. Over a list that can be read it adds no host: the list is as today. With
  neither — no readable answer and no `Creds:` terminal — nobody can be contacted in any case, and the command says
  so with the third wording of W5, under the code that exists (`no-remote-host-asked`).

**Why not read what can be read and go on.** A list with one unreadable entry may have held a refusal in that entry.
Going on would allow that host by itself in the next round. This is the case K6 names, and A1-T8 holds it.

## S1.4 — The one write: the commit, the merge, the stamp and the cap (K7)

```ts
/** A change for ONE host — never a list. `mend` is sent only by the change question, over a damaged list or a damaged file. */
export type HostChange =
  | { readonly kind: 'person'; readonly host: string; readonly answer: 'yes' | 'no' }
  | { readonly kind: 'forget'; readonly host: string }
  | { readonly kind: 'auto'; readonly host: string }
  /** A person's answer for one host over a damaged list: the readable rows are kept and this answer is applied, in the one write. */
  | { readonly kind: 'mend'; readonly host: string; readonly answer: 'yes' | 'no' | 'forget' };

export type Committed =
  /** `row`: the host's row after the write, or `undefined` when it was removed. `evicted`: the hosts whose row went at the cap. */
  | { readonly kind: 'written'; readonly row: HostAnswer | undefined; readonly evicted: readonly string[] }
  /** Nothing was written: the list read in this step already answers. `row` is the host's row there, if any. */
  | { readonly kind: 'kept'; readonly row: HostAnswer | undefined }
  | { readonly kind: 'full' }
  | { readonly kind: 'damaged' }
  /** The refusals file did not take the change (§ S1.5). `why` is one of three fixed words; no path and no error text. */
  | { readonly kind: 'unwritten'; readonly why: 'busy' | 'disk' | 'lost' };

/** One commit of one key: read now, merge, write. `merge` is pure and synchronous; nothing is awaited before the write is called. */
export type Merge<C, R> = (stored: unknown, change: C) => { readonly next: { readonly value: unknown } | undefined; readonly result: R };

export async function commit<C, R>(state: KeyValueStore, key: string, change: C, merge: Merge<C, R>): Promise<R>;
```

`KeyValueStore` is the seam of `src/sideSettings.ts:11-14` (F21), imported as a type. `commit` calls `state.get(key)`,
then `merge`, then — when `next` is given — `state.update(key, next.value)`, and only then awaits. It is the commit
of the parent's rule 1 (`:333-335`) and the generic primitive of C1 and C5. **It is the write of the LIST and of
the state row, in `globalState`. The write of the refusals file is § S1.5's, and the two are ordered by the keeper.
A change that ADDS a refusal writes the copy first, at once, and the file after it. A change that LIFTS one writes
the file first, and the list only when the file took it.** (The re-review's finding 0. The text of entry 1.2 wrote
the file first in both cases; a window that was closed while a "no" waited for a held lock then left nothing.)

**How one change of a host reaches the two places** (the keeper's `commit(change)`, § S1.5):

| The change | First | Then |
|---|---|---|
| `person`, "no" | THE LIST: the "no" row — the copy — in the step of the press, with no `await` before it. Not written in ONE case: the newest file this window has read already holds 100 other refusals; then the answer is `full` and nothing is written to either place | THE FILE: `refuse(host)`. `written` or `kept`: that is the result. `unwritten`: the copy stands, the host is held and marked (§ S1.5), and that is the result. `damaged`: the same as `unwritten` — the window holds it — unless the change is a `mend`. `full`, which the memory did not foresee: the copy stands alone and still refuses the host; the result is `full` |
| `person`, "yes" (*Read*) | THE FILE: the host's markers and this window's held entry are dropped, then `lift(host)`. On `unwritten` or `damaged`: stop here, the list is not touched, and that is the result | THE LIST: the person's "yes" row |
| `forget` | THE FILE: the same, then `lift(host)`. On `unwritten` or `damaged`: stop here | THE LIST: the row is removed |
| `auto` | nothing is written to the file. It was read just before (§ S1.6) and held no refusal for the host, and no change waits on the side | THE LIST: the automatic row, by the merge |
| `mend` | over a damaged list: the mend of the list, below | over a damaged file: the new file of § S1.3. Over a file that can be read: as `person` or `forget` |

**The merge, for one host.** "Fresh" is the host's row in the list read in this step.

| The change | fresh: no row | fresh: automatic "yes" | fresh: a person's "yes" | fresh: "no" |
|---|---|---|---|---|
| `person`, "no" | write "no" | write "no" | write "no" | write "no" (a new stamp) |
| `person`, "yes" | write a person's "yes" | write a person's "yes" (no `how`) | write (a new stamp) | write a person's "yes" — a person's press changes a "no" |
| `forget` | kept, nothing to do | remove | remove | remove — a person's press |
| `auto` | **write an automatic "yes"**, by the cap rule | kept | kept | kept — never changes a "no" |

(The row `repair` of the first text is gone with the parent's entry 1.2. A "no" row that is missing from the list is
not put back by anybody: the refusal is in the file.)

Every write for a host removes all rows of that host and puts one. Over a damaged list every row of this table is
`damaged`.

**`mend` is the one change that writes over a damaged list** (§ S1.3). In the one write: first the readable rows, one
for each host — the strongest, as § S1.3 reads two rows of one host — at most 100, the refusals first; then its
answer, as the `person` or the `forget` row of the table above, with the stamp and the cap below. If the answer cannot
be kept (a hundred refusals and a new host), the commit answers `full` and NOTHING is written: the list stays
damaged, and the person is told (W6). Over a list that can be read — another window mended it first — a `mend` is its
plain `person` or `forget` change. No other change, and no operation of the keeper, writes over a damaged value.
When both the file and the list are damaged, one `mend` mends both: the list first, then the file from the mended
list (§ S1.3).

**The stamp (J4).** An automatic row is stamped `now`. A person's row is stamped the later of `now` and one
millisecond after the `askedUtc` of the row it replaces. An `askedUtc` that `Date.parse` cannot read counts as no
time. So a row's time never goes back, whatever two clocks say. A refusal's `utc` in the file is taken the
same way, from the list as it was read just before the file is written. Nothing in this design decides by comparing
two stamps: the repair that did is gone.

**The cap (K7).** `HOSTS_MAX` stays 100. A write that would leave more than 100 rows drops, until 100 are left:

1. the oldest AUTOMATIC row — by `askedUtc`, then by host name — never the row being written;
2. if there is none, and the change is a person's: the oldest person's "yes";
3. if there is none: nothing is written, and the commit answers `full`.

A "no" is never dropped, by any change. So:

| At 100 rows | The change is automatic | The change is a person's |
|---|---|---|
| an automatic row exists | it goes; the new host is allowed (parent `:349`) | it goes |
| none; a person's "yes" exists | `full`: not allowed, not contacted (parent `:349-350`) | the oldest person's "yes" goes |
| 100 refusals | `full` | `full`: the answer is not kept, and the person is told (W6) |

A dropped "yes" loses nothing that protects: that host is allowed again by the round, or asked about again. In the
last cell the host is not contacted by the round either, because its automatic row cannot be written. It is the one
place where a person's "no" is not stored; it needs a hundred refusals, and it is said to the person. **The file has
the same cap and the same last cell**: at 100 refusals `refuse` answers `full`, and W6's second sentence is said. The
keeper sees a full file in the newest file it remembers and then writes to neither place; when another window
filled the file since, the copy that was written first stands alone and still refuses the host. The file never
drops a refusal to make room.

**Which test changes, and why.** `S3-T2` (`src/test/remoteRead.test.ts:170-183`) asserts today that a new host at the
cap drops the oldest row, and its fixture makes that row a "no" (`:172`, `:177`, `:179`). It is REPLACED by A1-T7,
not kept beside it: with the same fixture the "no" stays (`consentOf(next, 'oldest')` is `no`), and the row that
goes is the oldest person's "yes" (`h0`: five rows share the oldest day, and the host name breaks the tie). Kept, it
would hold the defect this story removes. `S3-T1`'s damaged half (`:162-167`) and `S3-T2b`'s two damaged lines
(`:188`, `:195`) are replaced by A1-T8 for the same reason: they assert that a damaged list is an empty one. The
summary names the three.

## S1.5 — The refusals file: its one writer, its lock and the sides (the parent's entry 1.2)

**This section is the parent's ledger entry 1.2** (2026-11-19, kinds `contract, approach`). It replaces the first
text of this section, *A refusal that is repaired, and a Forget in another window (K5)*. That text was the parent's
entry 1.1, and entry 1.2 supersedes it: no window writes a refusal "again", there is no guard, no timer of a guard
and no notice of a refusal restored. The last paragraph of this section keeps what the first text decided, as the
record. **Three parts of this section were rewritten at the re-review** (session `4082dc2c`; they are the baseline
of epoch 2, not entries): *The lock* (finding 3), *What the file did not take* with *The unsure marker* (finding 0),
and *The sides* with the copy-in (finding 1).

**Why a file.** A window sends the extension's WHOLE `globalState` object on any write, and replaces its whole copy
when another window's write reaches it (F27); how long that takes is not measured, and 20 s was seen (F28). So a
window that has not yet received a "no" erases it by writing ANY key, and nobody is told. The first text answered
with a repair by the window that gave the "no". The cadence consultation `e319fe2e` named the two cases where that
answer is nothing: the window that gave the "no" is closed before it looks again; and the Stop, which had no repair
at all. A1-T19 and A1-T20 are those two cases. A merge at the write does not help, because the window merges into
its own copy; a key of its own does not help, because the whole object is sent.

**What is kept where, and why the rest may stay in `globalState`.**

| What | Where | When a stale write loses it | When a stale write brings an old one back |
|---|---|---|---|
| A refusal | the file; a copy as the "no" row | The copy is lost. On this side nothing changes: the file refuses. Another side loses it (residual 2) | A lifted refusal's copy counts again: the host is refused until the person presses once more (residual 4) |
| The Stop | the file; a copy as `stoppedUtc` | the same | The round is stopped again; the list says so |
| A person's "yes" | `globalState` | The host has no row: with the setting on it is allowed again by itself, and shown as automatic; with it off the person is asked again | After a "no": nothing, the file refuses. After a *Forget* with the setting OFF: the host is read again with no question (residual 3) |
| An automatic "yes" | `globalState` | The host is allowed again; the daily notice can name it again | It counts only with the setting on, where the host would be allowed in any case |
| `toldUtc`, `lastAutoNoticeUtc` | `globalState` | A notice is shown once more | the same |

So every loss in the two last columns but ONE errs to a new question, a repeated notice or a host that is not read.
The one is residual 3, and moving the "yes" rows into the file would close it: the parent's candidate C4a. It is not
taken now because it is not a refusal that is lost there, and because it is the shipped behaviour of the session
door since 0.87.0 (F7): this story does not make it worse.

**The module.** NEW `src/remoteRefusals.ts` (the `remote` prefix: the log scan covers it, F23). Its rules are pure;
its writer goes through ONE seam, so a unit test runs it over a fake disk:

```ts
/** A change of the file — never a content. */
export type RefusalChange =
  | { readonly kind: 'refuse'; readonly host: string; readonly utc: string }
  | { readonly kind: 'lift'; readonly host: string }
  | { readonly kind: 'stop'; readonly utc: string }
  | { readonly kind: 'start' }
  /** The copy-in, at every read: the readable "no" rows of the list that the file does not hold, and the instant. Adds only. */
  | { readonly kind: 'migrate'; readonly rows: readonly { readonly host: string; readonly utc: string }[]; readonly utc: string }
  /** A file that went back: what this window remembers of the newest file it read. Adds only; `gen` ends above both. */
  | { readonly kind: 'regain'; readonly gen: number; readonly rows: readonly { readonly host: string; readonly utc: string }[]; readonly stop?: { readonly utc: string } }
  /** A person's answer over a DAMAGED file: the new file of § S1.3. It always leaves the round stopped. */
  | { readonly kind: 'mend'; readonly gen: number; readonly rows: readonly { readonly host: string; readonly utc: string }[]; readonly utc: string };

/** What one marker holds: a refusal or a Stop that the file did not take. `torn` is a marker that cannot be read. */
export type Waiting =
  | { readonly name: string; readonly kind: 'refuse'; readonly host: string; readonly utc: string }
  | { readonly name: string; readonly kind: 'stop'; readonly utc: string }
  | { readonly name: string; readonly kind: 'torn' };

/** The disk, as the writer needs it. The host half binds it to ONE folder; a test hands in a fake. */
export interface RefusalsDisk {
  /** The file's text. At most `REFUSALS_BYTES_MAX + 1` bytes are read, so a larger file is seen as too large and not loaded. */
  readonly read: () => Promise<{ readonly kind: 'absent' } | { readonly kind: 'text'; readonly text: string } | { readonly kind: 'failed' }>;
  /**
   * `claimFile` of `chatStoreLock.ts`, bound to the folder, the id `refused` and the rule `ownerGone`.
   * `take: 'person'` takes the lock whatever its owner. It is passed only on a person's press (*Save now*).
   */
  readonly claim: (doing: string, take?: 'person') => Promise<Claim>;
  /** Whether the lock on the disk still carries this claim's token (steps 5 and 7). */
  readonly stillOurs: () => Promise<boolean>;
  /** `writeFileAtomically` of `atomicFile.ts`, bound to the file. It throws what the disk refused. */
  readonly replace: (text: string) => Promise<void>;
  /** Removes this folder's beside-files (`.tmp`) older than `ms`. Never throws. */
  readonly sweep: (ms: number) => Promise<void>;
  /** Makes one marker by an EXCLUSIVE CREATE — no claim. `undefined`: the disk refused, or `UNSURE_MAX` markers are there. */
  readonly mark: (text: string) => Promise<string | undefined>;
  /** Every marker of the folder, read NOW. `failed`: the folder could not be listed, three times, 20 ms apart. */
  readonly marks: () => Promise<readonly { readonly name: string; readonly text: string | undefined }[] | 'failed'>;
  /** Removes one marker by its name. Never throws; a marker that is gone already is no error. */
  readonly unmark: (name: string) => Promise<void>;
}

export interface RefusalsStore {
  /** The file and the markers, read from the disk NOW. No claim. Never a value kept from an earlier read. */
  readonly read: () => Promise<RefusalsRead>;
  /** One change, under the claim, verified by a read-back. `written`, `kept`, `full`, `damaged` or `unwritten`. */
  readonly commit: (change: RefusalChange, take?: 'person') => Promise<Committed>;
  /** One marker for a change that was `unwritten`. `false`: no marker could be made (the disk, or the cap). */
  readonly mark: (waiting: Omit<Extract<Waiting, { kind: 'refuse' | 'stop' }>, 'name'>) => Promise<boolean>;
  readonly unmark: (name: string) => Promise<void>;
}

export function refusalsStore(disk: RefusalsDisk, clock: () => number): RefusalsStore;

export const CLAIM_ROUNDS = 5;            // a held claim is asked for again this often, 200 ms apart: about 1.6 s in all
export const CLAIM_FRESH_MS = 10_000;     // a claim older than this is not written under: the writer starts again (step 5)
export const WRITE_ROUNDS = 3;            // a change that is not there at the read-back is written again this often
export const BESIDE_STALE_MS = 60_000;    // a beside-file older than this was left by a crash
export const UNSURE_MAX = 32;             // markers in the folder; past it none is made
export const UNSURE_BYTES_MAX = 1024;     // one marker; a larger one is `torn`
```

(`RefusalsRead`, § S1.3, gained `waiting` at the re-review. `consentOf` reads its `file` only; what waits is the
keeper's question, below.)

**The merge — pure, `withChange(file, change, by)`.** It never returns a file with fewer refusals unless the change
is a `lift`, and never removes `stop` unless the change is a `start` (decision 20; A1-T21 holds both for every
change kind over generated files). Every file it returns has `gen` one higher than the file it was given — for
`regain` and `mend`, one higher than the higher of that and the change's `gen` — and `by` as it was handed in.

| The change | The file after | Answer |
|---|---|---|
| `refuse(host, utc)` | the host is absent: its entry is added. Present: nothing changes, the older `utc` stays | `written` · `kept` · `full` at 100 other hosts |
| `lift(host)` | every entry of the host is removed | `written` · `kept` when there was none |
| `stop(utc)` | `stop` is set when absent | `written` · `kept` |
| `start` | `stop` is removed when present | `written` · `kept` |
| `migrate(rows, utc)` | each row whose host is absent is added, the newest first, up to the cap; `migratedUtc` is set when it is absent and left when it is there. A row that did not fit stays a copy in the list, which still counts | `written` · `kept` when no row was missing · `full` when none fitted |
| `regain(gen, rows, stop)` | each row whose host is absent is added, up to the cap; `stop` is set when the change has one and the file has none. Nothing is removed | `written` · `kept` when the file already holds all of it |
| `mend(gen, rows, utc)` | ONLY over a damaged file: `{ v: 1, gen, by, migratedUtc: utc, refused: rows (at most 100), stop: { utc } }`. Over a file that can be read: nothing | `written` · `kept` |

A result of more than `REFUSALS_BYTES_MAX` bytes is `full`, and nothing is written.

**The write, step by step — `commit(change, take)`.**

1. **Claim.** `disk.claim(doing, take)`, where `doing` is the change's kind and nothing else — no host in a lock's
   note. Inside the claim a held lock is taken over only by the rule of *The lock*, below. `held`: ask again,
   `CLAIM_ROUNDS` times, 200 ms apart; then answer `unwritten: busy`. `failed`: `unwritten: disk`.
2. **Read** the file, under the claim. Damaged, and the change is not a `mend`: release, answer `damaged`.
3. **Merge**, with this write's own token as `by`. `kept` or `full`: release, answer it. Nothing was written.
4. **Sweep** the beside-files older than `BESIDE_STALE_MS`.
5. **Look at the claim once more**: it is younger than `CLAIM_FRESH_MS` by this window's clock AND
   `disk.stillOurs()`. Else release — which removes the lock only when it is ours (F30) — and start again at 1. A
   writer that slept, or whose lock a person took over, reads the file again before it writes.
6. **Replace**: `disk.replace(text)` — beside the file, then a rename over it (F31). A throw: release, answer
   `unwritten: disk`.
7. **Release** the claim — and note whether the lock was still this claim's. **A lock that was taken over between
   step 5 and here means that this writer renamed without holding it** (the pause after the ownership check, the
   re-review's finding 3): it does not answer; it starts again at 1, and so reads the file as it now is and applies
   its change to THAT.
8. **Read back, with no claim.** The file's `gen` and `by` are the ones this write made: `written`. They are not:
   another writer wrote since. Then **look for this change in the file as it now is**: for `refuse`, an entry of
   the host; for `stop`, a `stop`; for `lift`, none of the entries THIS write removed (by their `utc`); for `start`,
   not the `stop` this write removed (by its `utc`); for `migrate` and `regain`, every row of the change; for
   `mend`, a file that can be read and carries `migratedUtc`. There: `written`. Not there: start again at 1 — the
   change is merged again into the file the other writer left. After `WRITE_ROUNDS` rounds: `unwritten: lost`.

Step 8 is by identity for the two removals, so a person's new "no", pressed in another window between this
writer's rename and its read-back, is not taken for this writer's lift having failed, and is not lifted by a second
round. **A second merge cannot lose a refusal:** every change kind but `lift` only adds refusals, and a `lift` is
sent only by a person's press. What a second merge CAN lose is a lift; that errs to refusing.

**A read.** No claim: a rename is atomic, so a reader sees the old file or the new one. `disk.read()`: `absent` is
the empty file; `text` goes through `refusalsOf(text)`, the ONE parser (§ S1.2); `failed` is asked again twice, 20 ms
apart, and is then `damaged` for that decision — a file that could not be opened is not an empty file. Every
decision reads: `admit`, `consent`, `roundStop` and `standings` each call `read()` themselves, and the keeper
answers from no file that it kept between two of them. What it remembers of the newest file (*A file that went
back*, below) is used for one thing only: to add back what a stale writer removed. It never answers "not refused".

**The lock is the one this repository has, widened by a NAME and by a RULE** (`reuse-first.md`, move 1).
`claimConversation(dir, id, doing, now)` already takes any folder and any id that passes `isSafeId`; its four
sentences say "conversation", and it breaks a lock that is 30 s old (F30). So `chatStoreLock.ts` gains an exported
`claimFile(dir, id, doing, now, what, mayTake)`: the body of today's function with `what` in those sentences and
with `mayTake` where `breakIfStale` asks the lock's age today. `claimConversation` becomes
`claimFile(…, 'conversation', agedOut)`, where `agedOut` is today's 30 s test, moved and not changed: the chat store
behaves as before, and its own tests stay as they are, green. A second lock written here would be the copy that
drifts. Like every claim it first asks the data-root guard and the folder maker (`chatStoreLock.ts:294-300`); a
refusal by either is `failed`, and so `unwritten: disk`. The refusals claim passes the id `refused`, the words
`list of refused hosts`, and the rule `ownerGone`.

**The lock of this file is never taken over by its age (the re-review's finding 3).** The text of entry 1.2 used
the 30 s rule. A writer that had passed step 5 and then slept — a machine that sleeps, a process that is stopped —
was taken over after 30 s, woke up, and renamed its stale file over a refusal that another window had written
meanwhile. Step 5 cannot close that: no look comes after the last look. So:

- **The note names its owner.** Every lock note gains two fields, written by `claimFile` for every lock: `host` —
  the machine's name — and `boot` — the exact id of the machine's present boot where the platform has one (on
  Linux, the text of `/proc/sys/kernel/random/boot_id`), else absent. The chat store's rule does not read them.
- **`ownerGone(note)`** is true in two cases only, and both need a note that can be read and whose `host` is this
  machine's: (1) the note has a `boot`, this machine has one, and they differ — every process of an earlier boot is
  gone; (2) `process.kill(note.pid, 0)` throws `ESRCH` — no process has that number. Everything else is "not gone":
  a note that cannot be read, another `host`, `EPERM`, any other error, a process that answers. A process number
  that a later program was given, where no `boot` can be read, also answers and is "not gone" (V11).
- **A lock whose owner is gone is taken over inside the claim**, with the lock's own fence as today: the lock is
  read again just before it is removed, and removed only when it is still the note that was judged (F30).
- **Every other held lock is waited on, and then the write is `unwritten: busy`.** It is taken over only by a
  person's press — `take: 'person'`, which the keeper passes from `saveWaiting()` and from nothing else (X21). The
  same fence holds for that take-over.
- **What this costs.** A window that hangs with the lock, or a lock whose note is torn, blocks every write of the
  side until a person presses *Save now*. Meanwhile nothing is lost and no host is read by itself: the refusal is in
  its copy and in its marker (below). It fails closed, and it needs a person.

**A file that went back (finding 3, the second half).** After a person's take-over the former holder may still
live, and may still rename. So the keeper remembers the NEWEST side it has seen: after every read and every write,
when the file's `gen` is higher than the remembered one — or nothing is remembered — it keeps the file's `gen`,
`by`, refusals and Stop. At every read it compares:

| The file now, against what is remembered | What it means | What the keeper does |
|---|---|---|
| a higher `gen` | writes since, in order | remembers the new file |
| the same `gen` and the same `by` | nothing happened | nothing |
| the same `gen` and another `by` | two writers wrote on one baseline, and the other's rename came last | commits `regain` with what it remembers, before the decision answers |
| a lower `gen`, or no file where one was read | a stale writer renamed over a newer file, or the file was deleted | the same |
| the file is damaged | — | nothing is written (§ S1.3); the memory is kept for the mend |

Until that `regain` is `written` or `kept`, the remembered refusals and the remembered Stop count as in force in
this window, and a `regain` the file does not take is held and marked like any refusal the file did not take. The
`regain` only adds. So after a stale rename the file holds the union: the refusals of both, and the Stop of either.
A *Read*, a *Forget* or a start that the stale writer wrote over comes back as a refusal or a stop — it errs to not
contacting, and the list shows it (residual 4). What is left: a file that went back while NO window that had read
the newer file is open. The refusal is then only in its copy, and the copy-in brings it back while the copy stands
(residual 5b).

**Which of the lock's stated residuals apply here.** The header of `chatStoreLock.ts` states them (`:47-73`).

| What the lock's header says | Does it apply? | What this design does, and what is left |
|---|---|---|
| Two breakers of one stale lock can both come to hold it; both read one baseline and the last rename wins: "the loser's turn is overwritten on disk" | **Yes**, for two windows that both judge one owner gone. The loser's change — a refusal, a Stop or a lift — is then not in the file | Step 8: a loser that reads back after the winner's rename sees another `by` and writes again. A loser that read back BEFORE it sees the same `gen` with another `by` at its next read, and writes what it remembers (*A file that went back*). Left: a loser whose window ends in between; the copy in the list still counts (residual 5c) |
| A holder that stalls past the 30 s window is taken over (`:233-238`) | **No — not for this file.** A lock is not taken over by its age | A holder that only sleeps keeps its lock; others answer `unwritten: busy` and mark. Left: a holder that never wakes blocks the side until *Save now* (residual 5a), and the former holder of a lock that a PERSON took over can still rename (residual 5b) |
| A lock that is merely HELD is waited on for about 120 ms, then reported `held` (`:52-55`) | The wait applies. The chat store's consequence — a conversation minted again as a copy — does not | Step 1 asks again for about 1.6 s, then `unwritten: busy` |
| A writer killed between claim and release leaves its lock, broken after 30 s (`:49-51`); the chat store's sweep collects the debris (`:71-72`) | The first half applies with ANOTHER rule: the lock of a writer that was killed is taken over at the next claim, because its owner is gone — at once, with no 30 s. The sweep is the chat store's and never sees this folder | Step 4 is this folder's own sweep. A lock whose note was torn by the crash is not "gone" and waits for *Save now* |
| The lost turn is "kept in the window that wrote it", and the chat store saves it under a new id | The shape applies | A refusal the file did not take is kept as its copy, in its marker and by the window that took the press, until it is written |

**What the file did not take: `unwritten` (rewritten at the re-review, finding 0).** The text of entry 1.2 kept
such a refusal in the memory of the window that took the press, and nowhere else on the disk. The other windows of
the side went on reading hosts — the refused one too, once a stale write had erased its copy — and closing the
pressing window dropped even its own hold. Now three things hold a "no" or a Stop that came back `unwritten`:

1. **The copy**, in `globalState`. It was written before the file's write was awaited (§ S1.4), on every path.
2. **This window's memory**: a map from a host to its `utc`, and one Stop. While it holds any, each such host is
   refused in this window, the Stop is in force in this window, and `admit` answers `unwritten` for EVERY host.
3. **The unsure marker** — below — which says the same to every other window of the side, and outlives this one.

One notice is raised for each held item, W9, with the press *Save now*. A person's later *Read*, *Forget* or start
for a held item drops it from the memory and removes its markers first — it is the person's own press — and then
goes the ordinary road. For a *Read*, a *Forget* or a start that comes back `unwritten`, nothing is held and
nothing is marked: nothing changed, the refusal or the Stop stands, and the person is told (W6, W13).

**The unsure marker.** One small file for each change that waits, in the folder of the refusals file:

- **Its name and its text.** `unsure-<token>.json`, the token being this claim's own (a process number and a
  counter, as the lock's token is; no host in a file's name). Its text is `{ "v": 1, "kind": "refuse", "host":
  "<host>", "utc": "<UTC>" }` or `{ "v": 1, "kind": "stop", "utc": "<UTC>" }`, at most `UNSURE_BYTES_MAX` bytes.
  Anything else — not JSON, another `v`, another `kind`, a wrong shape, larger, or a read that fails — is `torn`.
- **How it is made.** By an exclusive create (`'wx'`), with no claim. So a lock that is held — the very thing that
  makes a write `unwritten: busy` — cannot stop it, and two windows never write one marker. It is never written
  over. When `UNSURE_MAX` markers are in the folder none is made: the side is stopped by those already.
- **Who reads it.** Every `read()` of the store lists the folder and reads every marker: the `waiting` of
  `RefusalsRead`. A folder that cannot be listed is `damaged`.
- **What it does while it waits.** A marker WAITS while its change is not in the file just read (for `refuse`: no
  entry of the host; for `stop`: no `stop`), and a `torn` marker always waits. **While any marker of the side
  waits, in every window of the side:** `admit` answers `unwritten` for EVERY host, so no automatic call is made and
  no automatic row is written; `consent` answers `no` for a host that a marker names and `damaged` for every other
  host, so the session door reads nothing either; `roundStop()` is `stopped` when a marker holds the Stop. It is
  what the pressing window does for its own memory (2, above), made true for the side.
- **Who lands it.** Every window. Before every `admit` and `consent`, and at every `commit` of a host's change
  (not at `commitKey`), the keeper tries each change that waits — its own memory's and every marker's — through
  the ordinary `commit` of the file. One that is `written`
  or `kept` leaves the memory, and its marker is removed with `unmark` AFTER the read-back showed the change in the
  file, never before. A marker whose change is already in the file is removed the same way. So a refusal that one
  window could not write is written by the next window that can, and the window that took the press may be closed.
- **A `torn` marker** is removed by nothing automatic: what it held may have been a refusal or the Stop. It goes
  only by `saveWaiting()` — a person's press — which then commits `stop` as well, so the round is left STOPPED, as
  a mended file is (§ S1.3), and W9's last sentence says that a refusal may have been lost with it.
- **`saveWaiting()` — the one person's press of this section** (X21). It commits every change that waits with
  `take: 'person'`, so a lock whose owner cannot be shown to be gone is taken over; it removes the markers it
  landed and the torn ones; it answers the `Committed` of the last write that was not `written` or `kept`, else
  `written`. It is bound to the action of W9 and to a press on a row that shows W12, and to nothing else.
- **How the person is told.** In the window that took the press: W9. In every other window of the side: the row
  W12 on each host of the accounts list, from the `unwritten` admission that S3 keeps (X18), and W9 once for each
  marker that this window did not make, with the `<why>` *another window of this side could not save it*. Both
  offer *Save now*.

**What the marker does not do — stated plainly (residuals 5a, 5d and 13).** It is a file on the same disk as the
file it stands in for. (1) **A disk that takes no write** — full, or read-only — takes no marker either: `mark`
answers `false`. The refusal is then in the copy and in the pressing window's memory, and NOWHERE else; the other
windows of the side read on unless the copy reaches them, and closing the pressing window leaves the copy alone,
which a stale write can erase. That is the defect of entry 1.2's first text, left for a disk that cannot be
written. W9 then says that THIS window must stay open, and the keeper tries the marker again with the write.
(2) **A folder that cannot be read** is `damaged` in every window: nobody is contacted. (3) **A marker stops the
whole side**, not one host: one "no" that a hung window's lock keeps out of the file stops the accounts list of
every window of that side until a person presses *Save now*. It errs to not contacting, and it is said.

**The keeper.** One for each window, built once in `bindRemoteSessions`. It is the bound form of the two modules:

```ts
export interface KeeperSeams {
  readonly state: KeyValueStore;              // the profile's globalState
  readonly key: string;                       // 'coai.terminalHosts'
  readonly refusals: RefusalsStore;           // the file of THIS side
  readonly now: () => string;                 // UTC ISO
  readonly allowNew: () => boolean;           // coai.terminalAccountsAllowNewHosts, read afresh at every call
  readonly changed: () => void;               // after every commit that wrote
  /** W9. `host` `undefined` is the Stop. `elsewhere`: a marker this window did not make. `marked` false: no marker could be made. */
  readonly unkept: (host: string | undefined, why: 'busy' | 'disk' | 'lost' | 'elsewhere', marked: boolean) => void;
}                                             // no seam for a damaged list or file: neither has a notice of its own (gate G2)

export interface HostKeeper {
  readonly stored: () => unknown;
  readonly commit: (change: HostChange) => Promise<Committed>;
  readonly commitKey: <C, R>(key: string, change: C, merge: Merge<C, R>) => Promise<R>;
  readonly admit: (host: string) => Promise<Admission>;
  readonly consent: (host: string) => Promise<Consent>;
  /** The Stop, in the file. S3 writes the copy in the state row after it (X17). */
  readonly stopRound: () => Promise<Committed>;
  readonly startRound: () => Promise<Committed>;
  readonly roundStop: () => Promise<'stopped' | 'running' | 'damaged'>;
  /** Each host's standing, from the file and the list read NOW. */
  readonly standings: () => Promise<readonly HostStanding[]>;
  /** How many refusals, the Stop among them, wait on this side: this window's memory and the markers, each change counted once. */
  readonly held: () => Promise<number>;
  /** A PERSON'S PRESS ONLY (*Save now*): takes the lock over, lands what waits, removes torn markers and then stops the round. */
  readonly saveWaiting: () => Promise<Committed>;
}

export function hostKeeper(seams: KeeperSeams): HostKeeper;
```

**The keeper runs its operations one at a time.** Every `commit`, `admit`, `consent`, `standings`, `roundStop`,
`stopRound`, `startRound` and `saveWaiting` of one window goes through one queue, so no decision of a window runs in
the middle of that window's own change: a lift is not met half-way by a copy-in of the same window (§ S1.2). The one
exception is the copy of a "no" or a Stop in the list, which is written in the step of the press, before the queue
is awaited (§ S1.4).

`standings()` answers a promise since entry 1.2: a host's standing is `no` when the file, the list, a marker or
this window's memory says so, and the file is read from the disk. `roundStop()` is `stopped` when the file holds
`stop`, when this window holds an unwritten Stop or when a marker holds one; `damaged` when the file or the folder
cannot be read; else `running`. It does not read the state row: that is S3's, and S3 puts the two together (X17).
There is no `guard`, no `guarded`, no `letGo` and no `restored`.

**The sides (F29).** The file is on the disk of the extension host that runs. A Windows window and a WSL window of
one profile are two hosts with two disks and ONE `globalState`. So:

- **On the side where the person pressed** the refusal is in the file. Nothing but a person's press in a window of
  that side removes it, and every window of that side reads it from the disk before every call: there is no lag
  and no stale view of the FILE. (A stale view of the list can still bring a lifted refusal back: residual 4.)
- **Another side** learns of it through the copy, honours it while the copy stands (decision 16), and — since the
  re-review's finding 1 — copies it into its OWN file at its next read (§ S1.2, *The copy-in*). From then on that
  side keeps it too. Inside the lag after the press a stale write can erase the copy before that side has read;
  that side then goes on reading the host (residual 2). After the lag every window's own copy of `globalState`
  holds the row, and every later write carries it.
- **A lift on another side** — *Read* or *Forget* pressed there — changes the copy and that side's own file. It
  cannot reach this side's file, so the host stays refused here until a press here. A refusal reaches the file of
  every side that reads while its copy stands, so it is lifted on each. W3 says on which side a refusal is kept,
  and the list of each side shows how the host stands there.
- **The Stop is not copied in.** `stoppedUtc` is a copy that every side honours while it stands; another side's
  file never gains a `stop` from it. An older build has no Stop to give, so finding 1 does not ask for it, and a
  round that is stopped on every side that ever saw the copy would need a start on each.
- **Why a copy IS copied in when it is seen — the opposite of entry 1.2's text.** That text refused the copy-in for
  two reasons: it makes a refusal hold on every side that ever saw it, and it brings a lifted refusal back from a
  window whose view lags (F28). Both are true, and both err to not contacting. The re-review weighed them against
  a refusal that is never in any file — an older build's "no" after the migration — and that a stale write erases
  for good. That one errs to contacting a host the person forbade. So the copy-in runs, and the two costs are
  residuals 2 and 4. Curing residual 4 needs a record of what was lifted. That is what is left of candidate C4b of
  the parent, with R8 and run (d) of R12 as its evidence; it is not built here.

**What the first text of this section decided, for the record (K5; the parent's entry 1.1, superseded).** The window
that wrote a "no" remembered it and wrote it again when it was missing — before every call, after every commit and
once in 60 s. It could not tell a *Forget* in another window from a lost "no", so it undid that *Forget* and said so
in a notice. It failed closed while that window lived, and protected nothing once it was closed; the Stop had no
such keeper. A1-T6 held it. All of it is replaced by this section.

## S1.6 — The round's door and the session road

```ts
export type Admission =
  | { readonly kind: 'read'; readonly how: 'person' | 'auto'; readonly allowedNow: boolean }
  | { readonly kind: 'refused' } | { readonly kind: 'not-asked' } | { readonly kind: 'full' } | { readonly kind: 'damaged' }
  /** A "no" or a Stop is not in the file yet — this window's own, or one a marker of the side holds: no host is contacted (§ S1.5). */
  | { readonly kind: 'unwritten' };

export interface HostStanding {
  readonly host: string; readonly answer: 'yes' | 'no'; readonly how: 'person' | 'auto'; readonly askedUtc: string;
}
```

A host that the file refuses and that has no row in the list has the standing `no`, `person`, with the refusal's
`utc` as `askedUtc`.

**`admit(host)`**, called by S3 just before EVERY accounts call, in this order:

1. Read the side from the disk: the file and the markers. A file that went back is written back first (§ S1.5).
   Try every change that waits — this window's memory and every marker (§ S1.5) — and read again. If one still
   waits → `unwritten`, for EVERY host and whichever window took the press. Nothing else is asked.
2. If the list can be read and holds a "no" row that the file does not: commit the copy-in (§ S1.2), and read the
   file again. A copy-in that is `unwritten` is held and marked, so the answer is `unwritten` as in step 1.
3. Read the setting (`seams.allowNew()`) and the list, now, and ask `consentOf(file, value, host, allowNew)`.
4. `no` → `refused`. `damaged` → `damaged`. `yes` → `read`, with the row's
   `how`, `allowedNow: false`. `ask` with the setting off → `not-asked`; nothing is written.
5. `ask` with the setting on → commit `{ kind: 'auto', host }` and await it. `full` → `full`. `damaged` →
   `damaged`. Else read the file AND the list ONCE more, and ask `consentOf` again in the step that answers: `yes` →
   `read`, `how` from the row, `allowedNow` true only when this commit was `written`; `no` → `refused`; `damaged` →
   `damaged`.

So the marked row is in the store before S3 can spawn (parent `:838-839`), an automatic row is never written over a
row (the merge), and the file is read from the disk as the last thing before the answer. What is left between that
read and the spawn is one step of this window: a "no" written to the file in that instant meets a call that has
started, which runs to its end and whose result S3 drops (residual 1). Whether the round is STOPPED is not asked
here: S3 asks `roundStop()` and its state row before every call, before `admit` (X17). Over a damaged list or a
damaged file `admit` and `consent` answer `damaged` and raise nothing: the keeper has no seam for a notice of damage
(J3, fitted at gate G2). S4 draws W11 and W12 from the admission that S3's round keeps for the host (X8, X18).
`admit` has no way to ask a person: `KeeperSeams` has no ask.

**The session road** (`src/remoteRead.ts`). Its store seam becomes:

```ts
readonly store: {
  /** Whether this host may be read now: `consentOf` over the refusals file, the list and the setting, all read afresh. */
  readonly consent: (host: string) => PromiseLike<Consent>;
  /** The person's answer to the question, committed. */
  readonly answer: (host: string, answer: 'yes' | 'no') => PromiseLike<Committed>;
};
```

`consent(host)` is steps 1–3 of `admit`, with a change that waits — in this window's memory or in a marker of the
side — answering `no` for the hosts it names and `damaged` for every other host: the session door reads nothing
either while the side cannot say what the file holds.
`consented` (`:366-370`): `yes` → go on; `no` → the sentence of today (`:79-80`); `damaged` → W6; `ask` → the
question. `asked` (`:356-364`): Escape writes nothing, as today; else `store.answer`; a result of `full`, `damaged`
or `unwritten` → its W6 sentence and no read; else as today. With the setting off, an automatic row gives `ask`, so
the door asks, and the person's answer replaces the automatic row. So a host that the file refuses is not read by a
press either, also when its copy in the list is gone. Nothing else of the road changes: the name re-check, the
three calls, the picked id, the sentences.

**The bound road** (`src/remoteSessionHost.ts`): `RemoteSessions` loses `answers` and gains `commitHost` (the
keeper's `commit`), `commitKey`, `admitHost`, `hostStandings` (a promise since entry 1.2), `stopRound`, `startRound`
and `roundStop` (X17), `saveWaiting` (X21; a person's press only), `changeHost(host)` (the three choices for one host;
the body of today's `changed`, `:136-141`, with commits), `askHost(host)` (the question for a host with no answer,
committed), `allowsNewHosts()` and `ignoredLayer()`. Over a damaged list or a damaged file `changeHost` opens the
change question with W7 before its detail and commits a `mend`, and `askHost` is `changeHost` (§ S1.3).
`changeHosts` stays. The change signal of today (`:165`) is the keeper's `changed`. `setReadsKey` is set from
`standings()` — after every commit, after every `consent`, and once at activation — because the file is read from
the disk. `bindRemoteSessions` is given the folder of the file (X20) and builds the `RefusalsDisk` over it: the one
place that names `refused.json`.

## S1.7 — The setting `coai.terminalAccountsAllowNewHosts` (K9)

**The manifest.** One property, after `coai.kimiAllowNetworkSignIn` (`package.json:669`):

```json
"coai.terminalAccountsAllowNewHosts": {
  "type": "boolean",
  "default": true,
  "scope": "application",
  "markdownDescription": "**Allow new hosts by themselves**, for the list of AI accounts of the terminal panel. On (the default): the host of an open `Creds: <host> / <session>` terminal that has no answer yet is allowed by the accounts round itself, with no question, and is read over ssh every 10 minutes; a notice names such hosts at most once a day, and **CoAI: hosts read over ssh** → *Not this host* forbids one. Off: you are asked first — no host is contacted without your own *Read*, a host with no answer says *not asked yet*, and a host that was allowed automatically counts as not answered until you press *Read*. Only your own **user** setting counts: a workspace's or a folder's `.vscode/settings.json` cannot switch it, and an imported config file is refused this key."
}
```

**The scope is `application`**, as `coai.teamServers` has (F15), not `machine` as the Kimi key has: it is a person's
decision about their own servers, one value for every window (appendix `:661-666`). We DECLARE the scope; what VS
Code then does with a workspace value, and what a WSL or remote window reads, is not observed (V3). The scope is the
second layer. The first is the read.

**The read: `globalValue` only.**

- `src/modelKeys.ts`: `SAFETY_KEYS` becomes `['kimiAllowNetworkSignIn', 'terminalAccountsAllowNewHosts']`. Its doc
  (`:26-36`) is widened: the settings that switch a PROTECTION of the person — a refusal of the server, or the
  question before a host is read — read from the person's own layer. It stays a list of its own beside `MODEL_KEYS`
  for the reason its doc gives: a key here names no program, so it raises no "a repository cannot choose a program
  for you" notice (`:33-35`). `USER_LAYER_KEYS` (`:52`) then holds the key, so `userLayerReader` answers it from
  `globalValue`, else the manifest default (`:107-109`), and the scan for a direct read covers it with no edit: its
  pattern is built from that list (F17).
- NEW pure, in `src/modelKeys.ts`: `ALLOW_NEW_HOSTS_KEY = 'terminalAccountsAllowNewHosts'`;
  `allowNewHostsFrom(value: unknown): boolean` — `true` for `true` and for `undefined`, `false` for everything else
  (`false`, `'true'`, `0`, an object). `undefined` is "the user layer holds nothing and no default was handed over",
  which is a window that still runs an older settings registry; the default of this build is on (owner decision 20).
  A `null` in the user layer reads as nothing held: the reader's `??` skips it (`:108`).
  `ignoredLayerOf(inspect, key): 'workspace' | 'folder' | undefined` — the layer of `ignoredIn` (`:130-136`), folder
  first.
- NEW host, in `src/sideConfig.ts` (one of the three files that are the door, F17):
  `allowsNewHosts(config)` = `allowNewHostsFrom(userLayer(config)(ALLOW_NEW_HOSTS_KEY))`, and
  `ignoredLayerFor(config, key)`. **`userLayer`, never `readerFor`**: `readerFor` answers from this side's overlay
  first (F13), which would be a second place the value can come from.
- The keeper's `allowNew` seam is `() => allowsNewHosts(vscode.workspace.getConfiguration('coai'))` — a NEW
  configuration object at every call. An object taken once would answer the value of that moment for ever.

**The ignored-layer sentence**, drawn by S4 as the first row of the accounts list when `ignoredLayer()` names a
layer (parent `:227-228`). The value is not read in either case, and the row may never appear (V3):

- workspace: *This workspace sets `coai.terminalAccountsAllowNewHosts`. It is ignored: only your own user settings
  count.*
- folder: *A folder of this workspace sets `coai.terminalAccountsAllowNewHosts`. It is ignored: only your own user
  settings count.*

**`NEVER_TRANSFERRED`** (`src/configTransfer.ts:30-55`) gains
`terminalAccountsAllowNewHosts: 'your own decision about which hosts are read over ssh without a question'`. So an
export never carries the key (`:108`) and an import refuses it by name with that reason (`:256-260`).

**K9 — where the key is listed, decided.**

| Place | Listed | Why |
|---|---|---|
| `package.json`, `"scope": "application"`, default `true` | yes | the parent, `:383-384` |
| `SAFETY_KEYS` → `USER_LAYER_KEYS` | yes | the parent, `:382`, `:387`; the read and the scan follow from the list (F12, F17) |
| `NEVER_TRANSFERRED` | yes | the parent, `:387-388` |
| `SETTING_ALIAS`, and its phrase in the English help | yes | F18. The phrase is `allow new hosts by themselves` |
| `src/settingsShape.ts`: `CoaiSettings`, `DEFAULTS`, `settingsFrom` | **no** (J5) | A key there must change the server's env block (`settingsReach.test.ts:104-116`); this one is not the server's, and nothing in `src_mcp` reads it. `settingsFrom` is fed by `readerFor`, the overlay road (F13). The other `application` keys are not there either (F15) |
| `NOT_FOR_THE_SERVER` (`settingsReach.test.ts:102`) | no | It lists keys OF `CoaiSettings`; this is not one |
| `OVERLAID_SETTINGS` (`settingsShape.ts:317`) | **no**, and a test holds it | One value for every window. A side's own value would live in `globalState`, where no person edits it |
| a control on a panel page | no | The parent gives it none; it is changed in the user settings, and the daily notice opens them |

**The help sentence the census needs** (S1's part; S5 rewrites the paragraph). One sentence at the end of the remote
paragraph of the chat article, in `helpContent.ts` and the four translations, with the bold label and the key in
English in every language: *"**Allow new hosts by themselves** (`coai.terminalAccountsAllowNewHosts`, on by default)
lets the accounts round read a host that has no answer yet; turn it off in your own user settings to be asked first.
A workspace cannot set it."*

## S1.8 — The words (K16), in English

All in `src/remoteHosts.ts` as one `HOSTS_SAID` table. The CODES stay string literals at their call sites in
`remoteSessionHost.ts`: a notice's code is never composed, and the census scan holds that (`src/notice.ts:29-36`).
The three button words stay where they are (`src/remoteSessionHost.ts:25-27`). The help in five languages is S5's.
`<day>` is `dayOf(askedUtc)`: the calendar day of that instant on this machine, `YYYY-MM-DD`, or *an unknown day*
when it cannot be read. `<n>` and the like are numbers.

| # | Where | The words |
|---|---|---|
| W1 | The question, code `read-remote-session`, and the change question, code `change-remote-host-answer`: the title | *Read the Claude session and the AI accounts on “<host>” over ssh?* |
| W2 | The question: the detail | *“Read” allows, with no further question for this host: reading the Claude session of its Creds terminals when you press take or add the question; and, every 10 minutes while such a terminal is open, reading which Claude Code, Codex and Antigravity accounts are signed in there, and their usage. For the usage, Claude Code is started on the host and asks Anthropic with the host’s own login; agy is started for its version. “Not this host” stops every ssh call of CoAI to this host.* |
| W3 | The change question: the detail — how the host stands, then W2, then *Forget* | One of: *Your answer now: Read, given <askedUtc>.* · *This host was allowed automatically on <day>; nobody was asked.* · *Your answer now: Not this host, given <askedUtc>.* Then W2. Then, setting on: *“Forget” removes the answer: the host then has none, and the next accounts round allows it again by itself.* Setting off: *“Forget” removes the answer: the host is then not read until you answer the question about it.* Then, since entry 1.2 (the sentence of the first text, about a window that writes a refusal again, is gone; its third sentence was added at the re-review): *A “Not this host” is kept on the side of this machine where you gave it — <side>. “Read” or “Forget” in any window of that side lifts it. If the host is still not read a moment later, another window had not caught up yet: choose it once more. On another side (Windows, a WSL distro, a remote machine) choose it there.* `<side>` is `thisSideLabel` of `src/consultantSides.ts:32`, as the MCP server tab names a side |
| W4 | *CoAI: hosts read over ssh*: a row's description | a person's "yes": *the Claude session and the AI accounts are read* · automatic, setting on: *allowed automatically on <day>: the Claude session and the AI accounts are read* · automatic, setting off: *allowed automatically on <day>; not read while coai.terminalAccountsAllowNewHosts is off* · "no": *not read* |
| W4b | The same row: its detail | a person's row: *asked <askedUtc>* (as today) · automatic: *nobody was asked; written <askedUtc>* |
| W4c | The same list over a damaged list: the row of a host of a live `Creds:` terminal for which no answer can be read | *no answer can be read for this host* |
| W5 | The notice of an empty list, code `no-remote-host-asked` (today: *"…asks once"*, `remoteSessionHost.ts:113`) | setting on: *No host is in the list yet. The host of a “Creds: <host> / <session>” terminal is allowed by itself at the first accounts round and is listed here then; choose it here to forbid it.* · setting off: *No host is in the list yet. The host of a “Creds: <host> / <session>” terminal is listed here once you answer the question about it.* · the list is damaged, no answer in it can be read and no `Creds:` terminal is open: *The list of hosts read over ssh cannot be read, and no answer in it can be read. No host is read over ssh while that is so. Open a “Creds: <host> / <session>” terminal and run this again: your answer for its host repairs the list.* |
| W6 | The session door, a list or a file that cannot be read; and an answer that could not be kept | *The list of hosts read over ssh cannot be read, so nothing was read. Run CoAI: hosts read over ssh and answer for a host there: your answer repairs the list.* · *The list of hosts read over ssh holds 100 hosts you refused, which is all it keeps, so your answer for “<host>” was not kept and nothing was read. Forget one of them in CoAI: hosts read over ssh, then answer again.* · since entry 1.2, a *Read* or a *Forget* that the file did not take: *Your answer for “<host>” could not be saved, so the host is still not read and nothing was read. Answer again in a moment.* |
| W7 | The change question over a damaged list — the SAME code `change-remote-host-answer`, the same title W1 and the same three buttons: what its detail starts with, before W3 | *The list of hosts read over ssh is damaged, so no host is read by itself until it is repaired. <n> answers can still be read, <m> of them “Not this host”. <u> entries cannot be read, and one of them may have been a “Not this host”. Your answer here repairs the list: the answers that can be read are kept, and the entries that cannot are dropped. A host of a dropped entry has no answer afterwards, and with coai.terminalAccountsAllowNewHosts on it is allowed again by itself. Look through the list afterwards.* (Before gate G2 this was a question of its own, with the code `mend-remote-hosts` and one button. That code and that button do not exist.) |
| W7b | The same question over a damaged FILE (entry 1.2): what its detail starts with, before W3. When the list is damaged too, W7 comes first | *The file of hosts you refused on this side (<side>) cannot be read, so no host is read from this side until it is repaired. <m> “Not this host” can still be read and are kept. A “Not this host” that was kept only in the file may be lost, and so may a stop of the accounts round. Your answer here writes the file again, with the accounts round STOPPED: look through CoAI: hosts read over ssh, then start the round from the list of AI accounts.* `<m>` counts the hosts of the shared list, of the newest file this window remembers and of the markers that can be read, each once |
| W8 | — | **Dropped at gate G2** (J3 fitted): a damaged list has no notice of its own, and the code `remote-hosts-damaged` does not exist. The number stays empty so that W9…W11 keep theirs |
| W9 | **Rewritten at entry 1.2** (it was the notice of a refusal written again, code `remote-host-refusal-restored`, which does not exist any more), **and again at the re-review** (findings 0 and 3: the side, the press). The notice of a refusal or a Stop that the file did not take, NEW code `remote-host-refusal-not-kept`, a warning, `subject` the host, or `accounts-round` for the Stop, with ONE action, *Save now* (`saveWaiting()`, X21) | Title, a host: *Your “Not this host” for “<host>” is not saved yet (<why>). No window of this side (<side>) reads a host over ssh until it is. Each window tries again by itself.* Title, the Stop: *The stop of the accounts round is not saved yet (<why>). No window of this side (<side>) reads a host over ssh until it is. Each window tries again by itself.* When NO marker could be made (`marked` false), the second sentence of either title is *This window reads no host over ssh until it is.* (the other windows were not told, so the title does not claim them; review of PR #1047, T1). Cure, a marker was made: *“Save now” writes it at once, also when another window holds the file and does not finish. If it still cannot be saved, check that the extension’s storage folder can be written.* Cure, NO marker could be made (`marked` false): *Only THIS window knows your answer: the disk refused the note that tells the other windows. Keep this window open, and check that the extension’s storage folder can be written. Other windows of this side may still read hosts.* Cure, after a marker that could not be read was removed by “Save now”: *A note that could not be read was removed. It may have held a “Not this host” or a stop, so the accounts round is now stopped: look through CoAI: hosts read over ssh, then start the round.* `<why>` is one of four fixed phrases, never a path or an error's text: `busy` → *another window is writing*; `disk` → *the disk refused the write*; `lost` → *the write did not stay*; `elsewhere` → *another window of this side could not save it* |
| W10 | The accounts list, the ignored layer (S4 draws it) | § S1.7 |
| W11 | The accounts list, a host's row over a damaged list or a damaged file (S4 draws it) | *the list of hosts cannot be read, so this host is not read. Choose this row and answer for this host: that repairs the list.* · over a damaged file: *the file of refused hosts cannot be read, so this host is not read. Choose this row and answer for this host: that repairs the file and leaves the round stopped.* |
| W12 | The accounts list, a host's row while a refusal or a Stop waits on this side — in this window's memory or in a marker (S4 draws it; entry 1.2; the re-review, finding 0) | *a “Not this host” or a stop is not saved yet on this side, so no host is read from it. It is tried again before every read. Choose this row to save it now.* The press is `sessions.saveWaiting()` (X21) |
| W13 | The accounts list, the row of the stopped list when a start was not kept (S4 draws it; entry 1.2) | the file is damaged: *The accounts round cannot be started: the file of refused hosts cannot be read. Answer for a host in CoAI: hosts read over ssh first.* · the start was `unwritten`: *The start could not be saved, so the accounts round is still stopped. Choose this row again in a moment.* · another side or an older copy stops it (`roundStop()` is `running` and the state row says stopped): the row of today, *The accounts round is stopped. Choose this row to start it.* |

The buttons *Read*, *Not this host* and *Forget*, the list's title and its placeholder do not change. No word of a
`notify(` argument in the `remote*.ts` files is one the log scan refuses (F23): a notice is passed
`HOSTS_SAID.<name>(host)`, and no variable there is called `rows`, `names` or `reason`. **No path of the
refusals file and no text of a disk error reaches a notice or a log line of these modules**: W9 says one of four
fixed phrases, and the path is said once, by the lock's own console line, as for every lock (F30).

## S1.9 — The test door and the host scenarios (K15)

**Decided: set by commits, restore by one raw write that exists only in test mode.**

```ts
export interface RemoteSessionsTestDoor {
  readonly replaceRun: (run: RoadSeams['run'] | undefined) => void;
  readonly read: RemoteSessions['read'];
  readonly pickedOf: RemoteSessions['pickedOf'];
  readonly readsKey: () => boolean | undefined;
  /** The product's own commit: a scenario gives a host its answer the way a press does. */
  readonly commit: RemoteSessions['commitHost'];
  readonly admit: RemoteSessions['admitHost'];
  readonly standings: RemoteSessions['hostStandings'];
  /** What is stored, as it is, for the `finally`: the list, and the TEXT of this side's refusals file (`undefined` when there is none). */
  readonly stored: () => Promise<{ readonly list: unknown; readonly file: string | undefined }>;
  /** TEST ONLY. Puts both back whole — the list by one raw write, the file by one raw write or a removal — removes every marker and the lock, and empties what this window holds as not saved and what it remembers of the file. */
  readonly restore: (before: { readonly list: unknown; readonly file: string | undefined }) => Promise<void>;
  /** The Stop, through the product's own road. */
  readonly stopRound: RemoteSessions['stopRound'];
  readonly startRound: RemoteSessions['startRound'];
  readonly roundStop: RemoteSessions['roundStop'];
}

export const testDoorOf = (sessions: RemoteSessions, state: vscode.Memento, folder: string): RemoteSessionsTestDoor => …
```

- `restore` is the ONE place of the extension that writes a whole value under `coai.terminalHosts`, and the ONE
  place that writes the refusals file without the writer of § S1.5 or removes it. It is in
  `testDoorOf`, which `extension.ts` calls only behind `ExtensionMode.Test` (`:935`; the guard test's pattern
  `remoteSessions\s*:\s*testDoorOf\(` still fits more arguments, `remoteExportGuard.test.ts:17`).
- Why not restore by commits: a commit cannot put back an automatic row, an old `askedUtc` or a refusal's `utc`, and
  no commit removes `migratedUtc`, so the profile would not be as it was found.
- A scenario runs in the editor's real profile folder, so the file it changes is the real one of that test profile.
  That is why `stored` takes the file's TEXT: a file this build cannot read is put back byte for byte.
- There is no `letGo` any more: no window guards a refusal (entry 1.2). The raw file write is the one lever that can
  weaken a refusal, so it is kept out of reach the same way: A1-T18 holds that the folder's name and the two raw
  operations are in `bindRemoteSessions` and `testDoorOf` and nowhere else.
- Why set-up is by commits: the scenarios then go the road a press goes — the claim, the merge and the cap included.

**The scenarios that change.** `terminalHostsScenario.ts`: `:55` `before = await door.stored()`; `:59` and `:67` become
`door.commit({ kind: 'person', host: HOST, answer: … })`; `:60` and `:68-69` read `await door.standings()`; `:87`
`door.restore(before)`. `terminalDoorsScenario.ts`: the same at `:59`, `:63`, `:73`, `:75`, `:78`, `:87`, `:96`,
`:112`; its two `withAnswer([], HOST, 'yes', …)` (`:63`, `:96`) replaced the WHOLE list by one row, and are now one
commit that leaves the other rows alone. The imports of `withAnswer`, `answersOf` and `consentOf` go.

## S1.10 — The security judgement

Decided before any S1 code. Each item says the decision, what it stands on, and what was NOT observed. Where an
effect is VS Code's and not ours, the words are "we declare" and "we read", not "it protects".

**What this story guards.** Since the owner's decisions O3, N2 and Q1 the store no longer stands before a host is
contacted (parent, finding F1, `:659-672`). What is left of a gate is the refusal. This story makes that one hold,
and it makes the automatic "yes" something only the person can take back.

**What this judgement got wrong first, and what it says now (the parent's entry 1.2).** The first text judged that
a refusal holds because the window that gave it writes it again (the old J-c), and it named the stale whole-list
write as residual 11, with a file as a candidate for after E2. The E1 plan gate said that the read-merge-write is
not atomic across windows; the planner rejected that; the cadence consultation `e319fe2e` showed two cases the
rejection could not answer — the refusing window closed before it looks, and the Stop, which nothing repaired. F27
is the cause, and it was in this repository's record before this spec was written. So the measure was applied to
the windows somebody thought of. The judgement now: **a refusal and the Stop are not kept in `globalState`**; they
are in a file that one function writes under a lock, that every decision reads from the disk, and that only a
person's press shortens. Residual 11 is gone, and the residuals of the file take its place below.

**What the judgement of entry 1.2 still got wrong, and what it says now (the re-review, session `4082dc2c`).** Three
findings, all accepted, and all of one kind: the file was right, and a road around it was left open. (0) A refusal
whose write failed was held by one window's memory; the measure was not on the side. (1) A "no" was copied from the
list once; a "no" that arrived later by the older road never reached the file. (3) The lock was taken over by its
age; a holder that woke up wrote a stale file over a refusal. The judgement now adds three rules. **A refusal that
is not in the file yet is in its copy at once and in a marker that every window of the side reads, and while it
waits nobody on that side is contacted. A "no" seen in the list is copied into the file at every read. No lock of
the file is taken over by its age, and a window that sees the file go back writes the union again.** What none of
them can do is in residuals 4, 5 and 13: a lift can be undone inside the lag; a disk that takes no write keeps a
refusal in one window; and one refusal that cannot be written stops the whole side until a person presses.

**An attacker who can name a terminal** — a program that can open a terminal in the window and call it
`Creds: <host> / x`: another extension, or a task of an opened workspace (V8). The name chooses a host from an
alphabet with no option and no shell character (F22), and nothing else.

| The host's row | The setting ON (the default) | The setting OFF |
|---|---|---|
| none | **The host is allowed by itself.** One automatic row is written. Within 30 s to 10 minutes the extension logs in there as the person and runs the fixed script, and again every 10 minutes while the terminal is open. The person is told afterwards: the daily notice, the list. This is F1, as the owner chose | **Nothing.** `not-asked`: no call, no row, no write. The list shows the host as *not asked yet* |
| automatic "yes" | read, as above | counts as no answer: **nothing**, until a person presses *Read* |
| a person's "yes" | read every 10 minutes while a terminal of that name is open — the opener decides WHEN a host the person once allowed is logged in to | the same: the setting does not take back a person's "yes" |
| "no" | **nothing, by any means of a name** | **nothing** |

What the name does NOT give under either value: the reply (it stays in this window's memory); a command, an option
or an operand; a person's "yes" — a row without `how` is written only by a press; a way around a "no". A hundred
invented names fill the store with automatic rows and push each other out; they never push out a person's row, and
a store of a hundred person's rows answers `full` (A1-T7). A name never writes the refusals file: only a person's
press adds a refusal, so a flood of names cannot fill it or reach its cap (A1-T21). The cost is noise: up to 100 rows, about 12 KB, and one
notice a day that names them.

One thing a name gains beyond the login, with the setting on: on that host the session door no longer asks (parent
`:353-356`). A session read still needs a person's press in that terminal.

With the setting off, a name gains one thing: the host appears in the list as *not asked yet*, and a person may
press it. The question then names the host and what a "yes" covers (W1, W2).

**An attacker that is another extension, beyond naming a terminal.** It cannot reach the store through this
extension: `globalState` is this extension's own; the refusals file is in this extension's storage folder, which
any code running as the person can write — as it can write `known_hosts` or start `ssh` itself, so the file is
not a boundary against such code and is not described as one; the test door is not an export outside test mode (F11); and no registered command takes a host or
an answer as an argument — `coai.terminalHosts` opens a list, and every change behind it is a person's press on a
modal (A1-T18 holds that the handler passes on no argument). It CAN, as far as we know (V4), write the user-level
setting itself. So the setting is not a boundary against code that already runs in the editor. Nothing in this plan
is: such code can start `ssh` itself.

**An imported config file.** It cannot decide the setting: the key is refused by name, with its reason, and an export
never carries it (F16; A1-T11). It cannot reach the store: the list of hosts is not a setting. It cannot plant a
side's own value that the read would take: the read does not go through the overlay (§ S1.7; A1-T11). A file
written by an older build does not know the key and cannot hold it.

**A workspace setting.** A `.vscode/settings.json` that sets the key, on or off, decides nothing: we declare the key
`application`, and — whatever VS Code does with that (V3) — the read takes `globalValue` and never the workspace or
folder value (A1-T11, the four mixes). Where VS Code hands the extension the ignored value, the list says so in its
first row. A workspace cannot write `globalState`. What a workspace can still do is name a terminal through a task
(V8); the table above is that case.

**The decisions, each with its ground.**

- **J-a. One function decides contact; one function writes.** *(security.md: the measure on the single road in.)* A
  second reader of the list would be a place where a "no" is looked up differently. A1-T2 scans for one, with a
  companion that finds the sanctioned sites.
- **J-b. An automatic "yes" is written only over no row, in the step of the write, and only after the file was read
  from the disk.** So a refusal of this side is never replaced or passed, whatever was read before an `await` and
  whatever this window's copy of `globalState` holds. *Not observed:* two real windows (R12).
- **J-c. A refusal and the Stop are kept in a file of the side, written by one function under an exclusive-create
  lock, read from the disk before every call, and shortened only by a person's press (the parent's entry 1.2).**
  *(security.md: the measure on the single road in.)* It replaces the first J-c — a missing "no" is written again
  by the window that wrote it — which protected nothing once that window was closed. What stands on our own code
  and is tested: the merge never shortens the file but for a lift or a start (A1-T21); a change is not reported as
  kept unless a read-back shows it (A1-T22); the two cases of the consultation (A1-T19, A1-T20); a writer that
  wakes up after a take-over does not lose another's refusal while a window that saw it is open (A1-T27). What
  stands on the disk and is NOT observed: that an exclusive create admits one writer and that a rename replaces the
  file whole on the person's own disk (V9; F30 says it was measured for the chat store, eight writers, one
  admitted); and what the system answers about a process that is gone (V11). So the measure is described as what
  the writer DOES — it claims, it writes, it reads back, it writes the union when the file went back — and never
  as "a refusal cannot be lost".
- **J-d. A list or a file that cannot be read contacts nobody and is never written over (K6).** The alternative that
  reads what it can is one unreadable entry away from allowing a refused host. It is mended only by a person's own
  answer for a host, and it has no notice and no question of its own (gate G2): so the damage is said where a person
  looks — a host's row, the session door, the hosts list — and nowhere else. A mended FILE leaves the round stopped
  (§ S1.3): what cannot be read may have been the Stop.
- **J-j. A refusal the file did not take is never reported as kept, and NO window of the side contacts anybody
  until it is (entry 1.2; widened at the re-review, finding 0).** The other way — say "kept" on the copy alone — is
  the defect entry 1.2 removes, made by hand. And entry 1.2's own text applied the measure to one window: the
  marker puts it on the side. *Not observed:* an exclusive create of the marker beside a held lock on a real disk
  (V9); A1-T26 runs it over the fake disk and, for the create itself, in a temporary folder.
- **J-k. The copy in `globalState` is honoured, never trusted, and copied into the file whenever it is seen
  (rewritten at the re-review, finding 1).** It is honoured, because another side and an older build have nothing
  else. It is never needed for a refusal to hold on the side that keeps it, because that side reads its file. And
  it is copied in at every read, because a copy can be erased for good and a "no" given by the older road would
  otherwise never be in a file. Entry 1.2's text refused the copy-in so that a stale view could not refuse a host
  the person had just allowed again. That power is now given, knowingly: it errs to not contacting, the list shows
  it, and one more press lifts it (residual 4).
- **J-m. No lock of the file is taken over by its age, and a file that went back is written forward (the re-review,
  finding 3).** A lock is taken over when its owner is shown to be gone on this machine, or by a person's press.
  The first is a judgement about another process, so only the two answers that cannot be wrong about a LIVE owner
  count — another boot, or no process of that number — and every doubt waits for a person. The second can leave a
  live former holder, so every window remembers the newest file it read and writes the union back. *Not observed:*
  what a real system answers (V11). It is described as "the claim asks whether the owner is gone", never as "a
  stale writer cannot write".
- **J-l. What stays in `globalState` is what is safe to lose** (§ S1.5, the table). One case is not safe and is
  named, not closed: residual 3.
- **J-e. The cap drops no refusal (K7).** Finding 6 of the gate round gave the installer's record the same rule: a
  row that goes takes its guard with it (appendix `:380-383`).
- **J-f. A marker this build does not know makes the list unreadable.** It is the same rule as J-d, applied to one
  row.
- **J-g. Only the user layer decides the setting, by the read and by the declared scope.** Two layers, and the read
  is the one we can test.
- **J-h. With the setting off, an automatic row is not a "yes" for the session door either.** One row means one
  thing for both readers (appendix `:592-596`); a second meaning would need `consentOf` to know who asks.
- **J-i. The door of the round cannot ask.** A timer that could raise a modal would train a person to press it.

**Residuals, named.** Rewritten at the parent's entry 1.2. The first text had eleven. Its 1 (one more call before a
refusal is seen), 2 (a refusal is guarded only while the window lives), 3 (a *Forget* in another window does not
hold), 9 (two clocks) and 11 (a stale whole-list write can erase a refusal before the repair) are GONE: each was a
residual of keeping a refusal in `globalState`. Residuals 1–8 below are the parent's eight of the file, with the
same numbers (parent § *Security*); 9–13 are this story's own.

1. **A call that is already running** when a refusal or the Stop is written runs to its end, 30 s at most; S3 drops
   its result. A call that starts afterwards is not made, in any window of that side: the file is read from the disk
   as the last thing before `admit` answers (§ S1.6).
2. **Another side of the machine** learns of a refusal or of the Stop through its copy in `globalState` only. It
   copies a refusal into its own file at its next read and keeps it from then on. Inside the lag after the press
   (F28) a stale write can erase the copy before that side has read; that side then goes on reading the host, and
   nobody is told. The Stop is not copied in: on another side it holds only while its copy stands. The side where
   the person pressed is not affected. The cure for the Stop is what is left of the parent's candidate C4b.
3. **A person's "yes" that was withdrawn can come back.** With the setting OFF, a *Forget* of a person's "yes" puts
   the host back to *not asked yet*; a stale write inside the lag can put the old "yes" row back, and the host is
   then read again with no question. *Not this host* is the press that holds. It is the shipped behaviour of the
   session door since 0.87.0 (F7), now with a timer behind it. The cure is the parent's candidate C4a.
4. **A lifted refusal can come back — as its copy, and since the copy-in also into the file.** After a *Read* or a
   *Forget*, another window still holds the old "no" row until its view catches up (F28). If it makes a decision in
   that time, it copies the row into the file of its side; and a stale write in that time puts the row itself
   back. Either way the host is refused again until the person presses once more, on each side that took it. The
   same follows a file that went back (§ S1.5): a lift that a stale writer wrote over returns as a refusal. It errs
   to not contacting, the list says so, and W3 tells the person to choose once more. The cure is a record of what
   was lifted — what is left of the parent's candidate C4b. The same holds for an old `stoppedUtc`: the round is
   stopped again, and the list says so.
5. **The lock, the marker and the writer (rewritten at the re-review; the parent's residual 5, with the same
   letters).** The lock is never taken over by its age. (a) **A lock whose owner cannot be shown to be gone** — a
   window that hangs with it, a note that was torn, another machine's lock over a shared folder, a process number
   given to a later program where no boot id can be read (V11) — makes every write of the side `unwritten: busy`
   until a person presses *Save now*. A "no" pressed then is in its copy and its marker, and no window of the side
   reads a host. (b) **After *Save now* the former holder can still wake up and rename its stale file over the
   new one.** A window that read the newer file sees a lower `gen`, or the same `gen` with another `by`, and
   writes the union back. A refusal is lost from the file only when no such window is open; the copy still counts
   and is copied in again, unless a stale write erased it too. (c) **Two windows that both judge one owner gone**
   can both write. The one whose change is missing sees it at its read-back or at its next read; left is a window
   that ends in between. (d) **A disk that takes neither the file nor the marker** keeps the refusal in the
   pressing window's memory and in the copy, and nowhere else. If THAT window is closed, the copy is all that is
   left, and a stale write can erase it. W9 says that the window must stay open.
6. **A damaged file, or a damaged list,** stops every accounts read of that side until a person mends it. The mend
   of a file keeps only the refusals the list still holds; one kept only in the file is lost, and W7b says so before
   the person answers. The mend of a list drops the entries that could not be read, and W7 says so; a refusal of
   THIS side is not lost by it, because it is in the file, and a copy that came from another side can be. **No notice says that either is damaged** (gate G2): a person
   learns it from a host's row, the session door or the hosts list. Until they look, every host is not read and
   nothing has told them why. It fails closed, and silently.
7. **A file that is deleted from outside** reads as a file that was never written. Every open window that had read
   it sees a file that went back and writes what it remembers; the copy-in copies the copies back at the next
   decision. With no window open, the refusals stand as their copies only until then. The same code can delete a
   MARKER, or EDIT the file; so it can edit `known_hosts`. Not a boundary of this story.
8. **A window still running an older build** reads an automatic row as a person's "yes", also with the setting off;
   drops the oldest row at its cap, a "no" copy among them; does not read the file, so ITS session door reads a host
   refused only there, on a press; and a "no" given in it is a row and no file entry until a window of this build
   on that side reads the store, which copies it in (§ S1.2; the re-review's finding 1). Until such a read it is a
   copy only, and a stale write can erase it. Until the old window is reloaded.
9. **A hundred refusals.** The next person's "no" is not kept, in the file or in the list. Said to the person; that
   host is not contacted by the round while the list is full.
10. **A press in flight** is not stopped by a "no" given during it: at most three calls, 15 s each, as today (F7).
11. **The setting can be written by other code in the editor** (V4), and Settings Sync may carry it to the person's
    other machines (V3). It is still the person's own layer.
12. **With the setting on, the first contact comes before any telling.** That is F1 and the owner's decision; this
    story does not change it.
13. **A side with a refusal that is not written reads no host at all** — every window of it, for as long as the
    file cannot be written, when a marker was made. One hung window's lock stops the accounts list of the whole
    side until the write lands or a person presses *Save now* — when a marker was made. When NO marker could be made (a
    disk that is full or read-only) only the pressing window stops reading, and its W9 notice says that other windows of
    the side may still read hosts. It errs to not contacting, and W9 and W12 say it. (Entry 1.2's text stopped one window; the re-review's finding 0 made it the side.)

**`SECURITY.md`** is S5's (stories document, S5 item 5). From this spec it takes: the merge and the cap; the
refusals file — where it is, that it is kept for each side, its one writer and its lock, that only a person's press
removes a refusal or the Stop, and why `globalState` alone cannot keep one (F27), with "the writer claims, writes
and reads back" and never "a refusal cannot be lost"; that a damaged list or file contacts nobody and that a mended
file leaves the round stopped; that a refusal the file did not take is in its copy at once and in a marker, stops
the reads of every window of that side, and is saved by *Save now*; that the lock is not taken over by its age;
that a "no" seen in the shared list is copied into the file; the setting, with "we declare" for the scope and "we
read" for the user layer; residuals 1–13.

## S1.11 — Scenarios

Unit tests run by `node --test` through `scripts/run-tests.mjs`; every test file starts with
`import '../../scripts/inRunFolder.cjs';`. **RED-GREEN-RED for every row**: the test is written first and seen RED
for the symptom in the *RED today* column; GREEN with the code; then each break-it of § S1.12 is applied to the
PRODUCTION line, seen RED with its message, and restored. All three observations go in the summary.

**How the first RED is a symptom and not a compile error.** Commit 1 of the brief moves today's consent functions
into `remoteHosts.ts` unchanged. Where a test needs a name that does not exist yet, the coder adds the thinnest stub
that keeps TODAY's behaviour — `consentOf` ignores its third argument, `commit` writes `withAnswer(...)`, `admit`
answers from `consentOf` and writes nothing. The *RED today* column is what that stub shows. It was read from the
code, not run.

**The fakes.** `H = 'h'`. A `backing` holds the stored values. A `view(backing, mode)` is one window's store: `get`
answers the view's own copy; `update` writes the copy and the backing; `sync()` copies the backing in. A view that
was not synced is STALE. `mode` is `'one-key'` (an update writes that key) or `'whole-state'` (an update writes
every key as this view holds it — what F27 says an editor does). A1-T3 and A1-T6 run in both modes; A1-T19 and
A1-T20 in `'whole-state'`. Every view marks the step:
`get` sets a flag and clears it in a microtask; `update` records whether the flag was set.

**The fake disk** (since entry 1.2) is one side's folder: a map of names to text with a clock the test moves. It is
a `RefusalsDisk` built over REAL `chatStoreLock.ts` rules wherever the test is about the lock — A1-T22 runs the
real `claimFile` and the real `writeFileAtomically` in a temporary folder of the test's own (`os.tmpdir()`, removed
in a `finally`) — and a plain in-memory one everywhere else, with switches: `replace` throws; a write of one writer
is dropped; the claim is handed to two; a read fails n times. A window that is "disposed" is a keeper and a view the
test drops; a "reload" is a new keeper and a new view over the same backing and the same disk. Two sides are two
disks over one backing. **Since the re-review** the fake disk also holds markers — an exclusive create and a
listing, each of which the test can make fail — and a lock's owner is alive or gone as the test says, through the
seam of `ownerGone` (`host`, `boot`, `alive(pid)`); and the test can HOLD a writer between any two of its steps and
let it go later.

Files: **RH** = `src/test/remoteHosts.test.ts` (new, pure). **RR** = `src/test/remoteRead.test.ts` (changed).
**AS** = `src/test/terminalAccountsSetting.test.ts` (new). **HW** = `src/test/remoteHostsWiring.test.ts` (new; source
scans of host code, each with a companion that the scan finds what it looks for).

| # | Name (file) | Input → expected | RED today |
|---|---|---|---|
| A1-T1 | a row says how it came, and a marker nobody knows is not read (RH) | A row with `how: 'auto'` → its standing is `auto`. A row with no `how` → `person`. The four row shapes 0.89.1 writes (a "yes", a "no", any string as `askedUtc`, an extra key) → `person`, and after a commit for ANOTHER host each is the same object. `how: 'later'`, and `how: 'auto'` beside a "no" → `consentOf` is `damaged`. `undefined` and `[]` → no standings, `ask` | *a row with a marker this build does not know was read as a person's "yes"* (F2) |
| A1-T2 | one function decides, and both doors ask it (RH, HW) | The table of § S1.3 over 12 stores × the two setting values × three files (empty; a refusal for the host; damaged): `consentOf` gives the listed word — with a refusal in the file it is `no` for every one of the 24, a person's "yes" row among them. For the same cases `keeper.consent` gives the same word, and `keeper.admit` gives `read` exactly where it is `yes` or where `ask` meets the setting on. Two rows of one host, one of them a "no" → `no`. HW: `TERMINAL_HOSTS_KEY` is named in `remoteSessionHost.ts` only, in the keeper's seams and in `testDoorOf`; no `.get(` or `.update(` of any source file is given the literal `'coai.terminalHosts'`; `consentOf(` is called in `remoteHosts.ts` and in `readsKey` only; `refused.json` is a literal of `remoteSessionHost.ts` only; companions: each scan finds its sanctioned sites | *an automatic row counted as an answer with the setting off*; the scan: *a second writer of coai.terminalHosts: `changed` in remoteSessionHost.ts*; with a `consentOf` that looks at the list alone: *a host refused in the file was answered “yes”* |
| A1-T3 | a "no" against an automatic "yes", in both orders, with a stale view (RH) | Windows A and B: two views over one backing `globalState`, ONE fake disk (one side). **(a) auto, then no:** B admits `h` (an automatic row, `allowedNow` true); A commits "no" → the file holds `h`; B, NOT synced, admits → `refused`, 0 updates by B. **(b) no, then auto, B's view fresh:** A commits "no"; B syncs; B admits → `refused`. **(c) no, then auto, B holding a list from before A's write — the case that lost the "no" in the first text:** A commits "no"; B, not synced, admits `h` → **`refused`**: the file is read from the disk, and B's stale list is not asked first. B then admits ANOTHER new host `g` → an automatic row for `g`, and in `'whole-state'` mode the backing's "no" row for `h` is gone — the copy is lost; B admits `h` again → still `refused`; A admits → `refused`; the file is the same file. **(d) the same loss by another key, `'whole-state'` only:** B, stale, commits the state row through `commitKey` → the backing's list is B's old one; `h` is `refused` in both windows. **(e) two refusals, each window stale to the other:** A refused `h`, B refused `g`; each writes its copy over the other's; the file holds both, and both hosts are `refused` in both windows | (c): *the store lost “Not this host” for h and the host was read*; (a), with a stub that writes "yes" over any row: *an automatic “yes” replaced a “Not this host”* |
| A1-T4 | who may change which row (RH) | Every cell of the merge table of § S1.4: the row after, and whether an update happened. A person's "yes" over an automatic row has no `how`. `auto` over any row → `kept`, 0 updates. `auto` over a "no" → the same "no" object. The table of § S1.4 that says how one change reaches the two places: for a `person` "no" the list's `update` is called in the step of the press, BEFORE the file's writer is awaited (the re-review's finding 0); for a *Read* and a *Forget* the file's writer is called BEFORE the list's `update`, and one that the file answers `unwritten` or `damaged` makes 0 updates. `forget` over no row → `kept`, 0 updates | *an automatic “yes” replaced a row* |
| A1-T5 | no list is ever handed to a write, and no content to the file's writer (RH, RR, HW) | Over every operation of the keeper (each change kind, `admit`, `mend`, `commitKey`, `stopRound`, `startRound`): every `update` was called in the step of a `get` of the same key; every `disk.replace` was called under a claim this writer holds, after a `disk.read` under that same claim. Type level: an object literal with `write` is not a `RoadSeams['store']`, and a `RefusalsFile` is not a `RefusalChange` (`@ts-expect-error`, both). HW: no source file exports `withAnswer` or `withoutHost`; `state.update(` is given the hosts key in the keeper's seams and in `testDoorOf` only; `writeFileAtomically(` is given the refusals path in the `RefusalsDisk` of `bindRemoteSessions` and in `testDoorOf` only | The step half is GREEN today — both writers read at the write (F7). RED today is the seam: *RoadSeams.store takes a list to write* |
| A1-T6 | a person's press lifts a refusal from any window of the side, and nobody writes it again (RH) — **rewritten at entry 1.2; it was "a refusal this window gave is written again"** | A commits "no" for `h`. **(a) the copy is lost:** the backing's row for `h` is removed behind A's back → `h` is `refused` in A and in B; NO update of the list follows from any `admit`, any commit for another host or any `commitKey` — the row is not written again by anybody; the file is the same file. **(b) *Read* in B, and A catches up first:** B commits a person's "yes" → the file holds no `h`; A syncs and admits `h` → `read`. **(b2) *Read* in B, and A reads inside the lag (residual 4, asserted so that it is seen; the re-review's finding 1):** B commits a person's "yes"; A, NOT synced, admits `h` → A's stale "no" row is copied into the file: `refused`, and the file holds `h` again; A syncs → still `refused`, in both windows; one more *Read* after the sync lifts it, and it stays lifted. **(c) *Forget* in B** (the case of the parent's entry 1.1), **A catches up first** → the file holds no `h`; after A syncs, A's `admit('h')` with the setting on writes an automatic row and is `read`; the "no" does NOT come back. **(c2) the same with A reading inside the lag** → the refusal is back in the file, as (b2). **(d) *Forget* in A**, with an `admit` of A queued behind it → the lift is not met half-way: the file holds no `h` and the list no row before that `admit` reads (the keeper's queue). **(e) an old "no" put back:** after (c), a stale `'whole-state'` write puts the "no" row back → `h` is `refused` again in both windows (residual 4), the next read copies it into the file, and one more *Read* lifts it. **(f) a lift that the file does not take:** the fake disk refuses the write → `unwritten`, the "no" row of the list is NOT touched, `h` is still `refused`, and through the road W6's third sentence, 0 runs. **(g) a damaged list:** the backing becomes `'junk'` → `h` is `refused` (the file), every other host `damaged` | (c), with the first text's guard left in: *a Forget in another window was undone*; (a), with a `consentOf` that looks at the list alone: *a “Not this host” whose copy was lost was read* |
| A1-T7 | the cap drops no refusal, and no person's row for an automatic one (RH) | **The fixture of S3-T2** (99 person's "yes", one "no" that is the oldest row) + a person's "yes" for a new host → 100 rows; `oldest` is still `no`; `h0` is gone. 99 automatic + 1 "no" + an `auto` commit for a new host → `written`, the oldest automatic row went and `evicted` names it; `admit` of one more new host → `read`. 100 person's rows + `admit(new)` → `full`, 0 updates, and the store is the same value. One "no" + 300 admissions of invented hosts → the "no" holds, 100 rows. 100 refusals + a person's "no" or "yes" → `full`; through the road → W6's second sentence, 0 runs. A changed answer of a known host at the cap drops nothing | *the list dropped a “Not this host” to make room* — what `:177` and `:179` of today's test assert |
| A1-T8 | a list that cannot be read allows nobody (RH, RR) | For each of `'junk'`, `null`, `7`, `{}`, `[null]`, `['h']`, `[{ host: 'h' }]`, `[yes for z, 'junk']`, `[no for x, { host: 'y', answer: 'maybe', askedUtc }]`, `[yes with how 'later']`: `admit` of a new host, setting on → `damaged`; `admit('x')` → `refused`; `admit('z')` → `damaged`; every change kind but `mend` → `damaged`; 0 updates in all, and the stored value is the same value; **no notice is raised: `KeeperSeams` has no member for one** (an object literal with `damaged` is not a `KeeperSeams`, `@ts-expect-error`). 100 admissions and a `commitKey` of another key leave the damaged value the same value. Through the road: W6's first sentence, 0 asks, 0 runs. **The mend is a person's answer (gate G2):** `{ kind: 'mend', host: 'q', answer: 'no' }` over `[yes for z, 'junk', no for x]` → ONE update; the stored value is `x`'s "no" (the same object), a "no" for `q` and `z`'s "yes", the refusals first; then `admit` works again. The same with `answer: 'yes'`, and with `'forget'` for `z` (its row is gone). Over 100 readable refusals and one bad entry: a `mend` for a NEW host → `full`, 0 updates, the same value; a `mend` with `forget` for one of the hundred → written. Over a list that can be read, a `mend` is its plain change. **Reaching the question:** `listedHosts` over `[yes for z, 'junk']` with the live hosts `z` and `q` → `z` with its answer and `q` with W4c, each once; over a list that can be read the live hosts are not added; over `'junk'` with no live host → no row, and the command says the third wording of W5. **One positive with the same fixture shape:** `[yes for z]`, not damaged → `admit('z')` is `read` | *a damaged list let a new host be allowed by itself*; and through the road: *a damaged list was asked about and then written over: 1 row left of 2* |
| A1-T9 | the store and the setting are read again before every call (RH) | Two admissions of `h` with a person's "yes": between them another view commits "no" and this one syncs → the second is `refused`. Two admissions of two new hosts: the `allowNew` seam turns false between → the first writes a row, the second is `not-asked` with 0 updates. `allowNew` was called at least once for each admission, after its read of the file. **The file is read before every call too:** two admissions of `h` with a person's "yes"; between them ANOTHER view refuses `h` and this one does NOT sync → the second is `refused`; `disk.read` was called at least once in each admission | *a “no” given between two calls did not stop the second* |
| A1-T10 | the setting, on and off (RH, RR) | **On:** `admit` of a host with no row → `read`, `auto`, `allowedNow` true; again → `allowedNow` false. With an `update` that resolves only when the test lets it, the admission does not resolve before it: the row is stored BEFORE a call can be spawned. **Off:** no row → `not-asked`, 0 updates; an automatic row → `not-asked`, and the row is still there; a person's "yes" → `read`. **On again:** the automatic row → `read`, `auto`, `allowedNow` false | *an automatic row counted as an answer with the setting off*; *the host was answered “read” before its row was stored* |
| A1-T11 | only the user layer decides, and nothing carries the setting in (AS; `modelKeys.test.ts`; `modelKeysAreReadOnce.test.ts`; `helpCoverage.test.ts`) | `allowNewHostsFrom(userLayerReader(merged, inspector(layers))(KEY))` for the mixes: nothing set → `true`; user `false`, workspace `true` → `false`; user `true`, workspace `false` → `true`; user unset, workspace `false` → `true`; user unset, folder `false` → `true`. User `'true'`, `0`, `{}` → `false`; `null` → `true`. `ignoredLayerOf` → `workspace`, `folder` (folder first when both), `undefined`. The manifest: boolean, default `true`, `"scope": "application"`; the description names the default, "asked first" and the user setting. `SAFETY_KEYS` deep-equals the two keys (`modelKeys.test.ts:67-73`, its name reworded). The direct-read scan matches `config.get('terminalAccountsAllowNewHosts')` (one more planted line, `modelKeysAreReadOnce.test.ts:45-50`). `NEVER_TRANSFERRED` holds the key with a reason of more than 20 characters; `importedConfig` of a file that carries it → refused with `never transferred:`; `exportedSettings` with a base value `false` → not exported. The key is not in `OVERLAID_SETTINGS` and not a key of `DEFAULTS`. HW: the body of `allowsNewHosts` in `sideConfig.ts` calls `userLayer(` and not `readerFor(`; the keeper's `allowNew` seam holds `getConfiguration('coai')` inside its arrow. `SETTING_ALIAS` has the key, its phrase is in the English help, and each of the five chat articles holds `` `coai.terminalAccountsAllowNewHosts` `` | *coai.terminalAccountsAllowNewHosts is not in package.json* |
| A1-T12 | the session doors (RR) | `remoteRoad` over a keeper and a fake store. Setting on, an automatic row → 0 asks, 1 run. No row → 1 ask. Setting off, an automatic row → 1 ask; *Read* → the row is a person's (no `how`) and 1 run; *Not this host* → "no", 0 runs; Escape → the row is still the automatic one, 0 updates, 0 runs. A person's "yes", setting off → 0 asks, 1 run | *an automatically allowed host was read without a question with the setting off* (F2) |
| A1-T13 | *Forget*, and *Not this host* on an automatic row (RH) | An automatic row; `forget` → no row; `admit` (on) → an automatic row again, `allowedNow` true. With the setting off → `not-asked`. A person's "no" over an automatic row → one row, no `how`; `admit` → `refused` | *Forget left the host allowed* |
| A1-T14 | the words (RH, HW) | Each of W1–W7, W7b, W9 and W11–W13 (W4c, the three wordings of W5, the three of W6, the two titles, the three cures and the four phrases of W9's `<why>`, and the three wordings of W13 among them; there is no W8) equals its text of § S1.8, for a host `h` and both setting values where it has two. The question keeps its code `read-remote-session` and its two buttons (HW: both are literals of the host file, and `notification-sites.json` holds the code). W5 does not hold `asks once`. `dayOf` of an instant built from this machine's LOCAL noon gives that local day; of `'x'` → *an unknown day*. HW: `remoteSessionHost.ts` holds no `Read the Claude session on` literal; each of the four codes (`read-remote-session`, `change-remote-host-answer`, `no-remote-host-asked`, `remote-host-refusal-not-kept`) is a string literal there, once; NO source file holds `mend-remote-hosts` or `remote-hosts-damaged` — a damaged list or file has no code of its own (gate G2) — nor `remote-host-refusal-restored` (entry 1.2); W3 holds the side's label and no sentence about a window that writes a refusal again; no W9 text holds a path separator | *the question still says “Read the Claude session on …”*; *the empty list still says the first read asks once* |
| A1-T15 | the door of the round never asks (RH) | `KeeperSeams` has no member whose name holds `ask` (an object literal with `ask` is not one, `@ts-expect-error`). 100 admissions of new hosts, setting on and off, over a keeper whose road-side ask throws → nothing is raised | with a stub that asks on `ask`: *the door of the round asked a person* |
| A1-T16 | the commit is generic (RH) | `commit(view, 'coai.terminalAccountsState', { stop: T }, merge)`: the value was changed after the caller last read it → `merge` is given the value of NOW; the step rule holds; a `merge` that gives no `next` → 0 updates. `keeper.commitKey` is that commit over the keeper's own `state`, and it calls nothing else: no disk read, no disk write | *the state row was written from a value read earlier* |
| A1-T17 | a person's answer is stamped after the row it replaces (RH) | A "yes" stamped one hour in the future; a person's "no" at `now` → its `askedUtc` is that stamp plus 1 ms. A row whose `askedUtc` is `'x'` → the new stamp is `now`. An automatic row → `now` | *an older “yes” looked newer than the “no” that replaced it* |
| A1-T18 | the host half is wired as written (HW; `remoteExportGuard.test.ts`) | `remoteSessionHost.ts` holds NO `setInterval(`: nothing here looks on a timer (entry 1.2; the guard's timer of the first text is gone). The handler of `registerCommand('coai.terminalHosts'` takes no parameter. `testDoorOf(` is built once, behind the test-mode check, with the `Memento` and the folder as its second and third arguments; `restore` is a member of the test door only. The literal `terminal-hosts` is in `extension.ts` once, and `refused.json` in `remoteSessionHost.ts` once; a raw write or a removal of that file is in `testDoorOf` only. `claimFile(` is called for the id `refused` in ONE place, and the name prefix `unsure-` is a literal of that same file only. **The press (the re-review; X21):** `'person'` is passed as a `take` in ONE place, inside `saveWaiting`; `saveWaiting(` is called in `remoteSessionHost.ts` in ONE place, the action of W9, and in NO file of the round (`remoteAccountsRound.ts`, `remoteAccountsHost.ts`); W9 has exactly one action. `kind: 'mend'` is written in `remoteSessionHost.ts` in ONE place, inside the change question's handler. `notification-sites.json` holds the ONE new code, `remote-host-refusal-not-kept`, NOT `remote-host-refusal-restored`, and `PLACES_THIS_SPEAKS` is the counted number (186) | *a refusal that could not be saved was not said* |
| A1-H1 | the store in a real editor (`terminalHostsScenario.ts`, extended; CI only) | `before = await door.stored()`. `door.admit('h1')` with the default setting → `read`, `auto`; a fresh `get` holds a row for `h1` with `how: 'auto'`. `door.commit` a person's "no" for `h1` → `door.admit('h1')` is `refused`, the stored row has no `how`, **and the refusals file on the real disk, read by `door.stored()`, parses and holds `h1`**. `door.stopRound()` → `door.roundStop()` is `stopped`; `door.startRound()` → `running`. In a `finally`: `door.restore(before)`, and `await door.stored()` deep-equals `before` — the list and the file's text | cannot be written today: the door has no `admit` |
| A1-H2 | the setting through the real configuration (the same file; CI only) | `getConfiguration('coai').update('terminalAccountsAllowNewHosts', false, Global)` → `door.admit('h2')` is `not-asked`, and no row for `h2` is stored. Back to `undefined` in a `finally` → `door.admit('h2')` is `read`; then `restore`. It records what `inspect()` answers for the key. If the editor refuses the update, the scenario FAILS and says so (V5) | — |
| A1-T19 | **the first check of the cadence consultation `e319fe2e`: the refusing window is gone, and a stale window writes** (RH) — added at entry 1.2 | `'whole-state'` views A and B over one backing, one fake disk. Both sync. **A commits a person's "no" for `h`. A is DISPOSED**: its keeper and its view are dropped, and nothing of A runs again. **B, stale** (not synced since before A's write), **commits an automatic admission** of a new host `g` — a whole-object write, which puts B's old list back: the backing holds no "no" for `h`. **Synchronize; reload both**: new views and new keepers are built over the backing and the SAME disk. **Admit `h` again**, in each → **`refused`**, 0 calls of the fake `call` seam for `h`, and no automatic row for `h` in the backing. The file holds `h` with the `utc` A wrote. Then the same with the setting off, and the same with `g` replaced by a `commitKey` of the state row | *a “Not this host” was erased for good by a stale window after the window that gave it was closed: h was allowed by itself and read* — what the first text's design does here, with its guard intact |
| A1-T20 | **the second check: the same for the Stop** (RH) — added at entry 1.2 | The same two views and one disk. **A calls `stopRound()`**, then writes the copy `{ stop: T }` through `commitKey`, as S3 does (X17). **A is DISPOSED. B, stale, commits `{ autoNoticed: T2 }`** through `commitKey` — the whole-object write of the consultation: the backing's state row has no `stoppedUtc`. **Synchronize; reload both.** `roundStop()` in each → **`stopped`**; S3's `stopped` seam, built as X17 says, is true with a state row that says running; a round over a fake `call` makes 0 calls and 0 admissions. `startRound()` in B → `running` in both, with no sync needed | *a stale window's notice write started a stopped round* |
| A1-T21 | nothing automatic shortens the file (RH) — added at entry 1.2 | **By construction:** for 200 generated files and every `RefusalChange` kind with generated arguments, `withChange` returns a file whose refused hosts are a superset of the input's unless the kind is `lift` (then: the input's minus that one host), and whose `stop` is the input's unless the kind is `stop`, `start` or a `regain` that carries one; `migrate`, `regain` and `mend` never remove a host of a readable file; the `gen` of every returned file is higher than the input's, and for `regain` higher than the change's too. **By use:** 300 admissions of invented hosts, a full list (the cap of § S1.4), 50 commits of `auto`, `commitKey` writes and two reloads over a file with one refusal and a Stop → the file's text is byte-equal; 0 calls of `disk.replace`. The cap: 100 refusals + `refuse` of a new host → `full`, 0 replaces, and through the keeper the list is not written either; a result over `REFUSALS_BYTES_MAX` → `full` | with a cap that drops the oldest refusal: *the file dropped a “Not this host” to make room* |
| A1-T22 | the writer: one at a time, and a change is not kept unless it is seen (RH) — added at entry 1.2 | The fake disk has an exclusive create, a rename and a clock. **Two writers at once** (`refuse h` and `refuse g`, interleaved at every step by the test's scheduler, every interleaving of the eight steps that the fake can order): the file holds BOTH, in every order. **The lock's first residual, forced:** both writers are let through the claim (the fake hands the lock to two) and the second rename wins → the first writer's read-back sees another `by`, does not find `h`, runs again, and the file holds both; `disk.replace` was called three times. **A lost change that stays lost** (the fake drops every write of one writer) → `unwritten: lost` after `WRITE_ROUNDS`. **A held claim:** held for 1 s → the writer waits and writes; held for 5 s → `unwritten: busy`, 0 replaces. **A lock is not taken over by its age (the re-review's finding 3; this cell had "a lock 31 s old is broken"):** a lock ONE HOUR old whose owner the fake reports alive is not taken — `unwritten: busy`, 0 replaces; the same lock with the owner reported gone is taken and the write lands; the same lock with `take: 'person'` is taken and the write lands. **A frozen writer:** the test's clock jumps 40 s between the read and step 5, and a write under a person's take-over landed meanwhile → the frozen writer does NOT replace; it starts again and both changes are in the file. **`gen` and `by`:** every file a writer leaves has a `gen` one higher than the file it read and its own token as `by`. **A disk that refuses:** `replace` throws → `unwritten: disk`, the file is the old file, and the claim is released. **The removals are by identity:** between a `lift h` writer's rename and its read-back another writer refuses `h` again → the lift answers `written` and `h` stays refused. **Beside-files:** one older than 60 s is swept by the next write; a younger one is left. `doing` in the lock's note is the change's kind and holds no host | *two windows refused two hosts at once and one refusal was lost*; with step 8 removed: *a refusal was reported kept and is not in the file* |
| A1-T23 | a file that cannot be read contacts nobody, and its mend leaves the round stopped (RH, RR) — added at entry 1.2 | For each of: not JSON; `[]`; `{}`; `{ v: 2, refused: [] }`; `{ v: 1 }`; `{ v: 1, refused: ['h'] }`; `{ v: 1, refused: [], stop: 5 }`; 101 refusals; 64 KiB + 1 byte; a read that fails three times → `admit` of a new host, setting on → `damaged`; of a host whose list row is a person's "yes" → `damaged`; of a host whose list row is a "no" → `refused`; `roundStop()` → `damaged`; `refuse`, `lift`, `stop`, `start` and `migrate` → `damaged`, 0 replaces, the bytes on the disk are the same bytes; no notice (`KeeperSeams` has no member for damage). A read that fails ONCE and then succeeds → the file, not damaged. **No file at all** → empty, not damaged; then the copy-in runs. **A folder that cannot be listed** (the markers cannot be read at all) → `damaged`, like a file that cannot be read. Through the road: W6's first sentence, 0 asks, 0 runs. **The mend:** the change question's `{ kind: 'mend', host: 'q', answer: 'no' }` over a damaged file and a list `[no for x, yes for z]` → ONE replace: the file is `v: 1`, `refused` holds `x` and `q`, `migratedUtc` is set, **`stop` is set**; `roundStop()` is `stopped`; `admit('z')` would be `read` once the round is started. The same with `answer: 'yes'` (the file holds `x` only, and is stopped) and `'forget'`. A `stopRound()` over a damaged file → `unwritten`, this window holds the Stop, and every `admit` is `unwritten`. `startRound()` over a damaged file → `damaged`, and S4's row is W13's first sentence. Both damaged — the list `'junk'` and the file not JSON → one `mend` mends both, the list first | *a file that could not be read let a host be allowed by itself*; *a mend started a round that may have been stopped* |
| A1-T24 | a "no" or a Stop the file did not take is not reported as kept, is in its copy at once, and that window contacts nobody (RH, RR) — added at entry 1.2; rewritten at the re-review (findings 0 and 3) | The fake disk holds a lock whose owner the fake reports ALIVE (a window that hangs). A commits a person's "no" for `h` → the list's `update` with the "no" row was called BEFORE the claim was asked for; the answer is `unwritten: busy`; `unkept('h', 'busy', true)` once; ONE marker is on the disk and names `h`; `keeper.held()` is 1. `admit('h')` → `unwritten`; `admit('g')`, a host with a person's "yes" → **`unwritten`**, 0 calls, 0 automatic rows; `consent('h')` → `no`, `consent('g')` → `damaged`. **The clock moves ONE HOUR → still `unwritten`:** nothing is taken over by its age. The fake reports the owner GONE → the next `admit` takes the lock over and writes the refusal first: the file holds `h`, the marker is gone, `held()` is 0, `admit('g')` is `read`, `admit('h')` is `refused`; `unkept` was called ONCE in all. **A *Read* for a held host** → it leaves the memory and its marker is removed, the list holds a person's "yes", nothing is written to the file for it later. **The Stop:** `stopRound()` with `replace` throwing → `unwritten: disk`, a marker of kind `stop`; `roundStop()` is `stopped` in THIS window AND in a second window on the same disk — the marker (entry 1.2's text had `running` there). **No marker either** (`mark` answers `undefined`): `unkept(…, false)`; this window answers `unwritten` for every host; a second window on the same disk still reads — residual 5d, asserted so that it is seen. **The window is closed while it holds one, with its marker made** → after a reload with the lock free, the first `admit` lands the marker and the file holds `h` (entry 1.2's text: the file has no `h`) | *a “Not this host” that was not saved was reported as kept*; *a window that could not save a refusal went on reading other hosts*; *a “Not this host” had no copy while its write waited* |
| A1-T25 | the copy-in at every read, the copy, and two sides (RH) — added at entry 1.2; rewritten at the re-review (finding 1; it was "the migration, once") | **The first copy-in:** a list with two "no" rows and three "yes" rows, no file → the first `admit` writes ONE file: both hosts with their `askedUtc` as `utc`, `migratedUtc` set, no "yes" host; the list is the same value; a second `admit` → 0 replaces. **An older window's "no" AFTER `migratedUtc`:** the other view writes a "no" row for `k` to the list only, as 0.89.1 does; this view syncs; its next `admit` of ANY host copies `k` in — the file holds `k`, and `migratedUtc` is still the first instant; `k` is `refused`. Then a stale `'whole-state'` write erases the row → `k` is STILL `refused`, in both windows and after a reload: the file. (Entry 1.2's text: the file was NOT changed.) A damaged list → no copy-in, `damaged`; after the list is mended the copy-in runs. A copy-in the disk refuses → the hosts are `refused` by their rows, each is held and marked, and the next `admit` tries again. A file deleted after the first copy-in → the next `admit` copies again from the rows. **Two sides:** two fake disks over ONE backing; side 1 refuses `h` → side 2, synced, `refused` by the copy, and after that read side 2's file HOLDS `h`; a stale write then loses the copy → both sides still `refused`. **The copy lost BEFORE side 2 has read** → side 1 `refused`, side 2 `read` — **residual 2, asserted so that it is seen and so that candidate C4b has its RED**. *Forget* on side 2 → side 2 allows `h` again, side 1 still `refused`; *Read* on side 1 → `read` on side 1. **The Stop is not copied in:** side 1 stops; side 2 is stopped by the copy, and its file has no `stop` | *the refusals of 0.89.1 were not carried into the file*; *a “Not this host” given in a window of an older build after the migration was never in the file, and a stale write let the host be read* |
| A1-T26 | **a refusal the file did not take stops the SIDE and outlives the window that took the press** (RH) — added at the re-review, finding 0 | `'whole-state'` views A and B over one backing, ONE fake disk, both synced; the lock is held by an owner the fake reports alive. **A commits a person's "no" for `h`** → `unwritten: busy`; the backing holds the "no" row; one marker. **A is DISPOSED.** **B, stale, commits a notice time through `commitKey`** — a whole-object write: the backing holds no "no" for `h`. Then in B: `admit('h')` → `unwritten`; `admit('g')`, a host with a person's "yes" → `unwritten`; a round over a fake `call` makes 0 calls and writes 0 automatic rows; `consent('h')` → `no`; `standings()` shows `h` as `no`; `unkept('h', 'elsewhere', true)` once. **B is reloaded** → the same. **The lock is let go** → B's next `admit` lands the marker's refusal: the file holds `h` with the `utc` A gave, the marker is gone, `admit('h')` → `refused`, `admit('g')` → `read`. **The disk refuses the file and takes the marker** (`replace` throws) → the same, until the disk writes again. **A marker whose change is already in the file** is removed at the next read, and stops nothing. **A torn marker** (its text is `{`): every `admit` of every window is `unwritten`; no `admit`, no commit and no reload removes it; `saveWaiting()` removes it and commits `stop` — `roundStop()` is `stopped`, and W9's third cure is said. **`saveWaiting()` over a lock whose owner lives** → the lock is taken, the refusal lands, the marker is gone. **The cap:** with 32 markers in the folder the 33rd `mark` answers `false`, and the side is `unwritten` by the 32. **The real create:** in a temporary folder, with the real lock held by another claim, `mark` makes its file, and a second `mark` of the same name fails | *a window that could not save a “Not this host” was closed, and another window of the same side read the host* — what entry 1.2's text does here |
| A1-T27 | **no lock is taken over by its age, and a writer that wakes up after a take-over loses nobody's refusal** (RH; the lock's own test file) — added at the re-review, finding 3 | **The rule, over its seam:** another `boot` → gone; `alive(pid)` throwing `ESRCH` → gone; `EPERM`, any other error and a process that answers → NOT gone; another `host` → NOT gone, whatever `alive` says; a note that cannot be read → NOT gone; no `boot` on either side and a process that answers → NOT gone. A lock ONE HOUR old with a live owner → `held` after the wait. `claimConversation` over the same fake still breaks a lock 31 s old and not one 29 s old: the chat store's rule did not change. **The pause after the ownership check:** writer A claims for `refuse g`, reads the file at `gen` 5, passes step 5, and the test HOLDS it before step 6. In window B a write with `take: 'person'` commits `refuse h`: the lock is B's, the file is `gen` 6 by B and holds `h`, B answers `written`. **A is let go**: it renames — the file is `gen` 6 by A and holds `g` and NOT `h`. A's step 7 finds that the lock was not its own: A does not answer; it starts again and its second round is `kept`. **B's next `admit`** meets `gen` 6 by A against the `gen` 6 by B it remembers → `regain`: the file holds `h` AND `g`, at a higher `gen`. Between A's rename and that write B's `admit('h')` is `refused` — the remembered refusal counts — and the fake `call` seam saw 0 calls for `h`. **B closed before its next read, and a third window C that had read `gen` 6 by B** → C writes the union. **No window that saw it** → the file holds no `h`; the copy refuses `h`, and the next read copies it in — residual 5b, asserted so that it is seen. **A lower `gen`:** the file is replaced by an older text behind the keeper's back → the union is written. **The file removed** → the same. **A lift that was written over:** B lifted `h` at `gen` 6 and A's stale file holds `h` → after the union `h` is refused again (residual 4, asserted) | *a writer that woke up after its lock was taken wrote a stale file over a “Not this host” given meanwhile*; with the 30 s rule left in: *a lock was taken over by its age* |

**What these do not prove.** Two real windows, and that an editor really sends its whole state (F27, V2: R12 (a)–
(c)). Two real sides (R12 (d)). An exclusive create and a rename on the person's own disk beyond the temporary
folder of A1-T22 and A1-T26 (V9). What a real system answers about a process that is gone, and what a boot id is on
the owner's machines (V11). What VS Code does with an `application` key in a workspace or a remote window (V3: R11).
A real older build beside this one: A1-T25 writes its row by hand. The modal as a person sees it (V6).

**Mapping to the stories document.** Every id A1-T1…T16 and A1-H1 is kept; A1-T14 is refined as X9 says. **A1-T6
keeps its number and changes its meaning at entry 1.2** (X9): it held the repair, and now holds that a lift holds
and that nobody writes a refusal again. A1-T17, A1-T18 and A1-H2 were added by this spec; **A1-T19…T25 were added at
entry 1.2**, A1-T19 and A1-T20 being the two checks of the cadence consultation. **A1-T26 and A1-T27 were added at
the re-review** (its findings 0 and 3), and A1-T4, T6, T21, T22, T24 and T25 were changed for it; the older
window's "no" after `migratedUtc` (finding 1) is in A1-T25.

## S1.12 — Break-its (a PRODUCTION change; each seen RED with its message, then restored)

| # | Change | Goes red |
|---|---|---|
| B1 | The row check ignores `how` | A1-T1 — *a row with a marker this build does not know was read as a person's "yes"* |
| B2 | `admit` looks at the rows itself and not at `consentOf` | A1-T2 — *the round read a host that counts as not answered* |
| B3 | An `auto` change replaces a row | A1-T3 (a), A1-T4 — *an automatic “yes” replaced a “Not this host”* |
| B4 | **(Rewritten at entry 1.2; it was "no guard after a commit".)** `consentOf` asks the list alone and not the file | A1-T3 (c), A1-T6 (a), A1-T19 — *a “Not this host” was erased for good by a stale window after the window that gave it was closed: h was allowed by itself and read* |
| B5 | **(Rewritten.)** `roundStop()` answers from the state row and not from the file | A1-T20 — *a stale window's notice write started a stopped round* |
| B6 | `await Promise.resolve()` between the read and the write in `commit` | A1-T5 — *the store was written in a later step than it was read* |
| B7 | **(Rewritten.)** `admit` keeps the file from its first read and does not read it again before it answers | A1-T9, A1-T3 (a) — *a “no” given between two calls did not stop the second* |
| B8 | **(Rewritten.)** A *Read* writes the list first and the file after it, and reports the list's answer | A1-T6 (f), A1-T4 — *a Read that the file did not take was reported as kept* |
| B9 | **(Rewritten at the re-review; it was the opposite change.)** The copy-in runs once, held back by `migratedUtc` | A1-T25 — *a “Not this host” given in a window of an older build after the migration was never in the file, and a stale write let the host be read* |
| B10 | **(Rewritten.)** A person's answer is stamped `now`, with no look at the row it replaces | A1-T17 — *an older “yes” looked newer than the “no” that replaced it* |
| B11 | The cap drops the oldest row of any kind | A1-T7 — *the list dropped a “Not this host” to make room* |
| B12 | An automatic change may push out a person's "yes" | A1-T7 — *an automatic row pushed out a person's answer* |
| B13 | A damaged list reads as its readable rows | A1-T8 — *a list with an entry that cannot be read allowed a host by itself* |
| B14 | A commit writes over a damaged value | A1-T8 — *a list that could not be read was written over* |
| B15 | `allowNew` is read once, when the keeper is built | A1-T9 — *the setting turned off between two calls did not stop the second* |
| B16 | `consentOf` ignores `allowNew` | A1-T10, A1-T12 — *an automatic row counted as an answer with the setting off* |
| B17 | `admit` answers before the write is awaited | A1-T10 — *the host was answered “read” before its row was stored* |
| B18 | `allowsNewHosts` reads `config.get(...)`; then `readerFor(...)` | the direct-read scan, then A1-T11 — *a workspace switched the setting*; *a side's own value decided the setting* |
| B19 | The key leaves `NEVER_TRANSFERRED` | A1-T11 — *an imported file decided the setting* |
| B20 | The manifest's scope becomes `machine`; the default becomes `false` | A1-T11, each by name |
| B21 | The road asks `consent` with the setting always on | A1-T12 |
| B22 | `forget` leaves an automatic row | A1-T13 — *Forget left the host allowed* |
| B23 | W1 goes back to today's title; W5 to *asks once* | A1-T14, each by name |
| B24 | `admit` raises the question on `ask` | A1-T15 — *the door of the round asked a person* |
| B25 | **(Rewritten; it was "the `setInterval` is removed".)** The notice of an unwritten refusal is not raised | A1-T24, A1-T18 — *a refusal that could not be saved was not said* |
| B26 | `testDoorOf` is built outside the test-mode check | `remoteExportGuard.test.ts` — its message of today |
| B27 | `restore` puts back the list and not the file | A1-H1 — *the profile was not left as it was found* (CI) |
| B28 | `admit` commits a `mend` when it meets a damaged list | A1-T8 — *a damaged list was repaired with no press* |
| B29 | A `mend` keeps the readable rows and drops the person's answer; then it puts the answer first and cuts at 100 | A1-T8 — *the answer that repaired the list was not kept*; *a mend dropped a “Not this host”* |
| B30 | The writer skips its read-back (step 8) | A1-T22 — *a refusal was reported kept and is not in the file* |
| B31 | The writer takes no claim | A1-T22 — *two windows refused two hosts at once and one refusal was lost* |
| B32 | The writer skips the look at its claim before the rename (step 5) | A1-T22 — *a writer that slept for 40 s wrote over a refusal given meanwhile* |
| B33 | `withChange` drops the oldest refusal at the cap; then: a `migrate` over a migrated file replaces `refused` with the rows | A1-T21 — *the file dropped a “Not this host” to make room*; *the migration removed a refusal* |
| B34 | A file that cannot be read reads as the empty file; then: a read that fails reads as the empty file | A1-T23 — *a file that could not be read let a host be allowed by itself* |
| B35 | A `mend` of the file leaves `stop` unset | A1-T23 — *a mend started a round that may have been stopped* |
| B36 | An `unwritten` "no" is answered `written` because the copy was written; then: `admit` answers `unwritten` only for the held host | A1-T24 — *a “Not this host” that was not saved was reported as kept*; *a window that could not save a refusal went on reading other hosts* |
| B37 | The copy-in removes the "no" rows from the list; then: it writes when no row is missing | A1-T25 — *the copy-in changed the shared list*; *a read with nothing to copy wrote the file* |
| B38 | `stopRound()` writes the state row and not the file | A1-T20 — *the stop was not in the file* |
| B39 | A "no" writes the file first and its copy after it | A1-T4, A1-T24 — *a “Not this host” had no copy while its write waited* |
| B40 | A refusal that is `unwritten` makes no marker; then: `admit` does not read the markers | A1-T26 — *a window that could not save a “Not this host” was closed, and another window of the same side read the host* |
| B41 | A marker is removed before the read-back shows its change; then: a torn marker is removed by `admit` | A1-T26 — *a marker was removed and its refusal is not in the file*; *a note that could not be read was dropped with no press* |
| B42 | `ownerGone` is true for a lock older than 30 s; then: for `EPERM`; then: for a note of another machine | A1-T27, A1-T24 — *a lock was taken over by its age*; *a lock was taken from an owner that was not shown to be gone* |
| B43 | The keeper remembers no file; then: step 7 ignores a lock that was taken over | A1-T27 — *a writer that woke up after its lock was taken wrote a stale file over a “Not this host” given meanwhile* |
| B44 | `admit` calls `saveWaiting` when a lock stays held | A1-T18, A1-T27 — *a lock was taken over with no press* |
| B45 | `claimConversation` passes `ownerGone` | the lock's own tests — their message of today for a stale lock that is not broken |

## S1.13 — The brief: order, files, commands

**No step of this brief is started while the parent is stopped** (its entry 1.2): the re-review and the owner's
approval come first, and the brief may change with them. Steps 3b and 4 were rewritten at the stop.

One commit for each step; `npm run compile` exit 0 and the whole suite green at each.

1. **Move.** NEW `src/remoteHosts.ts` with today's consent section (`src/remoteRead.ts:28-38`, `:112-147`, `:160-166`) moved
   unchanged; `remoteRead.ts` imports what it uses. No behaviour changes; every test of today is green.
2. **Read.** `hostsOf` (private), the row check with `how`, `consentOf` with the file, `allowNew` and `damaged`,
   `standingsOf`, `dayOf`. A1-T1, the table half of A1-T2, the read half of A1-T8. `S3-T1`'s damaged half and
   `S3-T2b`'s two lines are replaced. (`consentOf` is given an empty file until step 3b lands.)
3. **Write.** `commit`, the private merge, the stamp, the cap. A1-T3 (a) and (b), T4, T5 (the list's half), T7, T16,
   T17. `S3-T2` is replaced by A1-T7. `withAnswer` and `withoutHost` are deleted here.
4. **The file (entry 1.2; the re-review).** In this order, each its own commit: (a) `src/chatStoreLock.ts` —
   `claimFile` with the word in its sentences and with the rule `mayTake` where the lock's age is asked today; the
   note's two new fields; `agedOut` (today's 30 s test, moved and not changed) and `ownerGone` over its seam;
   `claimConversation` calling `claimFile` with `agedOut`; the lock's own tests green and unchanged; the rule half
   of A1-T27. (b) NEW pure half of `src/remoteRefusals.ts` — `refusalsOf`, `waitingOf` (the marker's parser),
   `withChange`, the postcondition of step 8, `textOf`; A1-T21, the read half of A1-T23. (c) the writer
   `refusalsStore` over `RefusalsDisk`, with `mark`, `marks` and `unmark`; A1-T22, with the real lock and the real
   atomic write in a temporary folder, and the real exclusive create of a marker (A1-T26, its last cell).
5. **Keep.** `hostKeeper` over both, its operations through one queue: `admit`, `consent`, `commit` in the order of
   § S1.4 (the copy first for a "no" and a Stop, the file first for a lift), `commitKey`, `stopRound`,
   `startRound`, `roundStop`, `standings`, the memory of what the file did not take, the markers and their landing,
   `saveWaiting`, the memory of the newest file with `regain`, the copy-in, `mend`. A1-T3 (c)–(e), T6, the rest of
   T8, T9, T10, T13, T15, T19, T20, the rest of T23, T24, T25, T26, the keeper half of T27.
6. **The road.** `RoadSeams.store` in `remoteRead.ts`; the three W6 sentences; `readsKey`. The `world()` of
   `remoteRead.test.ts` (`:107-121`) is built over a keeper, a view and a fake disk. A1-T12.
7. **The setting.** `modelKeys.ts`, `sideConfig.ts`, `package.json`, `configTransfer.ts`, the help sentence in five
   languages, `SETTING_ALIAS`. A1-T11.
8. **The host.** `remoteSessionHost.ts`: the `RefusalsDisk` over the folder it is given — the one place that names
   `refused.json`, with the read capped at `REFUSALS_BYTES_MAX + 1` bytes; the keeper bound; the words; the list's
   rows — over a damaged list or file the readable answers and the hosts of the live `Creds:` terminals, through
   `liveTerminals` and `credsSession`, which exist; the change question, which over damage starts with W7 or W7b
   and commits a `mend`; the ONE notice (W9), with its action *Save now* bound to `saveWaiting`; the `RefusalsDisk`'s
   marker half and the seam of `ownerGone` (the machine's name, the boot id where there is one, `process.kill`);
   NO timer; `changeHost`, `askHost`; the test door.
   `extension.ts:161`: the folder handed to `bindRemoteSessions`; `:935`: `testDoorOf(remote, context.globalState,
   folder)`. `node scripts/count-notifications.mjs --write`, and `PLACES_THIS_SPEAKS` raised by one, to 186, with a
   dated comment that names the code `remote-host-refusal-not-kept`. A1-T14, T18. Keep `liveNameOf` as it is (F26).
9. **The host scenarios.** § S1.9; A1-H1 and A1-H2. They run only in CI's real-editor job; its result is read from
   that job on the head commit.

Lint: `complexity` 4 and 50 lines for a function. The merge is one small function for each change kind behind a
table of the kinds, as `FOLLOW` is in `remoteRead.ts:415-429`; `admit` is the five steps of § S1.6 as five
functions, and the writer's eight steps of § S1.5 are eight. No suppression is added. `remoteRefusals.ts` and
`remoteHosts.ts` stay under 400 lines each: the words table is the first thing to move out if one does not.

Commands, from `src_vs_code/`: `npm ci`; `npm run compile` (read the exit code); `node
../.agents/conventions/tools/low-priority.mjs node scripts/run-tests.mjs out/test/remoteHosts.test.js`; the same
for `out/test/remoteRefusals.test.js` and for the lock's own test file; `node
../.agents/conventions/tools/low-priority.mjs npm test`; `npm run lint`. From the worktree root: `node
.agents/conventions/tools/plan-lifecycle.mjs`.

## S1.14 — Growth

- **The store**: at most 100 rows, about 120 bytes each, about 12 KB. Automatic rows push each other out; a person's
  "yes" goes only for a person's newer answer; a "no" goes only by a person's press.
- **The refusals file — NEW at entry 1.2, one for each side** (`terminal-hosts/refused.json`). **Projected size:**
  at most 100 refusals and one Stop, about 76 bytes for an entry with a host name of 30 characters: about 8 KB, and
  never more than `REFUSALS_BYTES_MAX` (64 KiB), because a write that would pass either cap answers `full`. Only a
  person's "no" adds an entry: a terminal's name does not, so a flood of names cannot grow it (A1-T21). **Who
  retires it:** an entry goes by a person's *Read* or *Forget*, the Stop by a person's start; nothing else. The file
  itself is kept for ever, 64 KiB at most on each side, in the extension's own storage folder.
- **The lock, `refused.lock`**: present only while a write runs, under 300 bytes. **Interrupted:** a writer that is
  killed leaves it; the next writer takes it over at its next claim, because its owner is gone (§ S1.5) — not by
  its age. A lock whose owner cannot be shown to be gone stays until a person's *Save now*; meanwhile a read takes
  no claim, so reads go on, and what cannot be written is marked. No timer.
- **The unsure markers, `unsure-<token>.json` — NEW at the re-review (finding 0).** One for each refusal or Stop
  that the file did not take. **Projected size:** at most `UNSURE_BYTES_MAX` (1 KiB) each and `UNSURE_MAX` (32) in
  the folder: 32 KiB. Only a person's "no", a person's Stop, a "no" row to copy in or a `regain` makes one; a
  terminal's name does not. **Who retires it:** the write that lands its change, in any window of the side, after
  its read-back; a person's *Read*, *Forget* or start for what it holds; `saveWaiting` for a torn one. None goes by
  its age: a marker with no window to land it stays, and stops the side at the next start of a window, which then
  lands it. **Interrupted:** a window that ends between the create of a marker and its write leaves a torn one; it
  stops the side until *Save now*.
- **What a window remembers of the file**: the newest file it has read — its `gen`, its `by`, at most 100 refusals
  and one Stop — replaced at every newer read. It goes with the window.
- **A beside-file, `refused.json.<pid>.<n>.tmp`**: one for each write, renamed away or removed in the same call
  (F31). **Interrupted:** a writer killed between its write and its rename leaves one, 64 KiB at most; every later
  write removes the beside-files of this folder that are older than 60 s (§ S1.5, step 4). At most one for each
  crash, until the next write on that side.
- **What a window holds as not saved**: one entry for each refusal the file did not take, and one Stop; an entry
  leaves when its write lands or when the person lifts it; it goes with the window.
- **Notices**: one for each refusal or Stop the file did not take, in each window of the side — each needs a disk
  that refuses or a lock held for more than 1.6 s. None for a damaged list or file. All go through the one door,
  which keys repeats by code and subject (F19).
- **Interrupted, the list**: a window that closes in the middle of a commit leaves the old value or the new one,
  never half of a list. **Interrupted, a change of both places**: a window that closes between the copy's write
  and the file's leaves a "no" row with no file entry — the next read of any window of this build on that side
  copies it in. A window that closes between a lift's write of the file and its change of the list leaves the "no"
  row there: the next read copies it back into the file, and the host is refused until the person presses again
  (residual 4). No state between needs a sweep.

## S1.15 — What the Opus reviewer checks

1. No function of `src/` outside `testDoorOf` writes a value under `coai.terminalHosts` that was not built from a
   read in the same step — by reading `commit`, not by the test alone.
2. The merge table of § S1.4, cell by cell, against the code; that `mend` is sent only by the three answers of the
   change question over damage; and that NO change kind called `repair` exists.
3. **The file (entry 1.2):** `withChange`, kind by kind — nothing but a `lift` removes a refusal and nothing but a
   `start` removes the Stop; the writer's eight steps in their order, the claim released on every path; the
   read-back by `gen` and `by`, and by identity for the two removals; that no caller ANSWERS from a file kept across
   a decision, and that what the keeper remembers of the newest file is used only by `regain`.
4. The order in the keeper's commit: for a "no" and a Stop the copy first, in the step of the press, and the file
   after it; for a lift the file first, and a *Read* or a *Forget* that the file did not take touches nothing.
5. The cap never drops a "no": every path that shortens the list, and that no path shortens the file.
6. Damage: every door, for the list and for the file, and that no commit but a person's `mend` writes over either.
   A mended file is stopped. No notice, no question and no code of its own exists for damage (gate G2).
7. A refusal that is `unwritten` is never answered as kept, and `admit` answers `unwritten` for every host while
   the window holds one OR a marker of the side waits. A marker is removed only after a read-back showed its
   change; a torn one only by `saveWaiting`. `take: 'person'` reaches the claim from `saveWaiting` only, and
   `saveWaiting` is called from a person's press only.
8. The setting is read through `userLayer`, afresh, and through no overlay; a NEW configuration object at each call.
9. The three replaced tests are gone, not kept beside A1-T7 and A1-T8.
10. Every break-it was seen red with the message of § S1.12.
11. The rows X1–X22 are in the stories document and in the S3 and S4 briefs.
12. No sentence of a doc comment says what VS Code does with the scope, what a disk does with a rename, or that a
    refusal "cannot" be lost. The words are "the writer claims, writes and reads back".
13. `chatStoreLock.ts` changed by a name and a rule: `claimConversation` passes today's 30 s test, moved and not
    changed; no constant, wait or fence of the chat store changed, and its tests are the ones of before. The
    refusals claim passes `ownerGone`, and NOTHING about this file's lock is decided by an age.
14. No path of the refusals file and no text of a disk error is in a notice or in a log line of the `remote*.ts`
    modules.
15. The copy-in runs at every decision and writes only when a row is missing; `regain` only adds; every operation
    of the keeper goes through its queue, and the copy of a "no" is the one write before it.
16. `ownerGone`: only another boot id or `ESRCH`, on a note of this machine, is "gone". The doc comment says what
    the claim ASKS, not that a stale writer cannot write (V11).

## Definition of Done — E1.S1

- [ ] **The parent's stop is over**: the re-review of epoch 1 ran (session `4082dc2c`, `proceed`; its findings 0, 1
      and 3 are in this spec), the owner approved epoch 2, and the parent's ledger shows it. No S1 code before that.
- [ ] The caller read "Changes to the parent" and J1–J6 BEFORE any S1 code, and recorded in the parent's ledger any
      item judged a change. (Gate G2, 2026-11-19: K5 with J2 is entry 1.1; J3 is fitted; the rest is inside. Then
      entry 1.2, the refusals file, which supersedes 1.1 and stopped the plan.)
- [ ] This spec was reviewed by Opus — gate G2 for the text before entry 1.2, and AGAIN for the file design of
      § S1.2–S1.6 and S1.8–S1.15 with the re-review's three findings, which gate G2 has not seen — and X1–X22 are in
      the stories document and in the S3 and S4 briefs.
- [ ] A1-T1…T27 green — A1-T19 and A1-T20 (the two checks of the cadence consultation), A1-T26 (two windows, the
      pressing one disposed) and A1-T27 (the pause after the ownership check) among them; each RED and each
      break-it B1…B45 observed with its message; all in the summary, with the three replaced tests named
      (`S3-T1`'s damaged half, `S3-T2`, `S3-T2b`'s two lines).
- [ ] `chatStoreLock.ts` changed by a name and a rule, its own tests green and unchanged.
- [ ] `npm run compile` exit 0; the whole suite and lint green; no suppression count grew;
      `notification-sites.json` generated again.
- [ ] A1-H1, A1-H2 and the two rewritten scenarios green in CI's `extension · a real editor` job on the head commit,
      read from that job.
- [ ] The help sentence is in five languages; the rest of the documents are S5's.
- [ ] `node .agents/conventions/tools/plan-lifecycle.mjs` clean.
