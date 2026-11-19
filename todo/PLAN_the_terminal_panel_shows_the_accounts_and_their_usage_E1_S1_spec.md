# PLAN — E1.S1 spec: the shared store — a "no" that always wins, the automatic "yes", the setting (SECURITY SPEC)

> Status: **plan only, nothing implemented yet, 2026-11-19.** The security spec of story E1.S1 of
> [PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E1.md](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E1.md)
> (parent [PLAN_the_terminal_panel_shows_the_accounts_and_their_usage.md](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage.md),
> § 3 and § *Security*; its appendix, § G). Written by Fable before any S1 code (the security row of
> `subagent-models.md`: authorisation, outside the cornerstone quota). It holds the security judgement on who is
> contacted with the person's keys (§ S1.10), which must stand before S1 is coded. It keeps no change ledger: a change
> is an entry in the parent (epoch 1 is open with ONE change, 1.1, which is K5 of this spec; the next accepted change
> is a full stop). Reviewed by Opus at gate G2 on 2026-11-19
> ([PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E1_G2.md](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E1_G2.md));
> its verdict is applied in this text: K5 is the parent's entry 1.1, and a damaged list is mended with no notice and
> no question of its own. It is promoted with the E1 stories document. Parent line numbers (`:n`) are the parent's
> as it stands with entry 1.1.
>
> Scope: `src_vs_code` only. NEW pure `src/remoteHosts.ts`; `src/remoteRead.ts` (the road's store seam);
> `src/remoteSessionHost.ts`; `src/modelKeys.ts`; `src/sideConfig.ts`; `src/configTransfer.ts`; `src/extension.ts` (one
> argument); `package.json` (one setting); one help sentence in five languages; the tests; two host scenarios.
> Coder: Sonnet. Review: Opus. Read at `b4aa611b` (no file under `src_vs_code/` differs from `8494bb0d`, where the
> stories document was verified). All paths are under `src_vs_code/` unless they start with `research/`, `todo/`,
> `.agents/` or are a file of the repository root. **Nothing was compiled or run for this spec:** the worktree has no
> `node_modules`. Every "RED today" below is read from today's code, not observed.

## Changes to the parent

**One: K5. It is entry 1.1 of the parent's ledger** — 2026-11-19, `reviewer` (the judge of gate G2), kind `approach`:
a *Forget* given in another window does not lift a refusal while the window that gave it is open. This spec first
said "none". Gate G2 judged otherwise: the parent could be read both ways (its § 3, point 2 against point 3), the
stories document had read it the other way (its A1-T6), and in doubt it is a change. The parent's text says it now
(§ 3, *How a person forbids a host*, `:400-404`; the test plan, `:840-842`; R8, `:981`). J2 (the notice W9), the
sentence of W3, X9 and R8's run condition have the same cause and are the same entry. **Epoch 1 has one change; one
more stops the plan.**

**Two things were fitted to the parent's text at gate G2, so that neither is a change.** A damaged list is mended by a
person's own answer in the change question that exists, with no notice and no question of its own (J3, § S1.3): this
spec first added the notice `remote-hosts-damaged` and the modal `mend-remote-hosts`, and both are gone. And nothing
here moves U10 (the stories document, K3).

**K5 is decided without a new row kind.** A person's *Forget* in another window and a lost "no" leave the same store:
no row. This spec adds nothing to the store that would tell them apart. The window that wrote the "no" treats both as
a loss: it writes the "no" again, and it says so in a notice. What is told apart, and is honoured, is every case the
row of today can show: a person's *Read* in any window (a person's later "yes"); a *Forget* or a *Read* in the window
that wrote the "no"; and any press after that window was closed or reloaded. So the refusal fails closed. The cost
is named in § S1.5: a *Forget* does not hold from another window while the window that refused the host is open.

**The other way, not taken: a tombstone.** A fourth row kind that reads as "no answer" and that the repair honours
would make a *Forget* hold from every window. It is a change of the parent — kind `contract`: the parent's row is
`{ host, answer, askedUtc }` plus `how: 'auto'` (parent `:324`), and *Forget* "removes the row" (parent `:400-404`,
`:840-842`). It would be a second entry, and so a full stop. It also costs more than its entry: an older build reads such a row as a real answer or as a damaged list
(F2, F3), and it needs its own cap rule and its own retirement. It does not make a refusal safer. If the owner wants
*Forget* to hold across windows, that is the change to record, and § S1.4 (the merge table) and § S1.5 (rule 2 of the
repair) are where it goes.

**Inside the parent's text, named so the caller can judge each.** None changes the scope, a goal, the Definition of
Done, a merge condition or the parent's row. Gate G2 judged each (its § 1.2): J1, J4, J5 and J6 are inside; J2 is
part of entry 1.1; J3 is inside once it has no surface of its own.

| # | What this spec does | The parent's sentence it stands on |
|---|---|---|
| J1 | The repair has a third moment: once in 60 s while the window holds a refusal (§ S1.5). The parent names two moments, "before every call and after every commit" (`:340`). A window whose only host was just refused makes no call and no commit: its timer is cleared (parent `:285`). Without the third moment that window never looks | "A refusal is repaired, never lost" (`:339`) |
| J2 | A notice each time a refusal is written again (§ S1.8, W9). It is how a person learns why a *Forget* did not hold, and it is the record R12 can read. **Part of the parent's entry 1.1:** it exists only to say K5 | "What the person is told" (`:363`) |
| J3 | A damaged list contacts nobody, is never written over, and is mended only by a person's own answer for a host, in the change question that exists (§ S1.3). The parent does not discuss a damaged list (K6). **Fitted at gate G2:** no notice and no question of its own — a new surface was a change in doubt | "Never overridden" (`:322`) |
| J4 | A person's answer is stamped later than the row it replaces (§ S1.4). Two windows of one profile can have two clocks (F20) | "A person's "yes" beats an automatic "yes"… Only a person's own press changes a "no"" (`:336-338`) |
| J5 | The key is NOT listed in `src/settingsShape.ts` (§ S1.7, K9). The parent lists that file (`:387`). A key there must reach the server's settings file (F14) and is read through a per-side overlay (F13) | "Only the person's own user-level value counts" (`:382`) |
| J6 | At a full list a person's answer may push out the oldest person's "yes", never a "no" (§ S1.4, K7). The parent speaks only of the automatic row | "The cap protects the person's answers" (`:348`) |

## Changes to § 4 and § 5 of the stories document (for gate G2)

The caller puts these into the stories document and into the S3 and S4 briefs in ONE commit, before S3 or S4 is
coded. The rows are X1…X16; X14…X16 were added at gate G2, from its findings C-2, C-3 with S-2, and its § 4. **They
are in the stories document since 2026-11-19** (its § 4 and its S3 and S4 briefs).

| # | Where | The stories document says | This spec says | Who must follow |
|---|---|---|---|---|
| X1 | C1, the file | The pure half is in `src/remoteRead.ts` | The store is in NEW pure `src/remoteHosts.ts` (the `remote` prefix, so the log scan covers it). `remoteRead.ts` is 509 lines today and would pass 800 | S3, S4 import from `./remoteHosts` |
| X2 | C1, `HostChange` | Three kinds: `person`, `forget`, `auto` | Two more, sent only by S1 itself: `repair` (a refusal this window wrote, written again) and `mend` (a person's answer for one host over a damaged list, which repairs it in the same write) | S3 and S4 send none of the two |
| X3 | C1, `Committed` | Named, not defined | § S1.4: `written`, `kept`, `full`, `damaged` | S3 (the state row uses its own result type) |
| X4 | C1, `consentOf` | "stays the one function" | `consentOf(value, host, allowNew)`; `Consent` gains `'damaged'` | — |
| X5 | § 2, the store seam | `RoadSeams.store` `{ read, write(rows) }`; `AnswerStore`; `RemoteSessions.answers` | `RoadSeams.store` is `{ consent(host), answer(host, answer) }`. `withAnswer`, `withoutHost`, `answersOf`, `AnswerStore.write` and `RemoteSessions.answers` are gone. No function of the product takes a list to write | S3's change-signal listener reads `hostStandings()` |
| X6 | C1 and C5, the commit primitive | "reusable for another `globalState` key" | The pure `commit(state, key, change, merge)` is exported. S3 writes the state row through the BOUND `sessions.commitKey(key, change, merge)`, never through the pure one: the bound one runs the repair after the write (§ S1.5) | S3 |
| X7 | C1, for the list | `ignoredLayerOf(key)` (pure, in `modelKeys.ts`) | Pure `ignoredLayerOf(inspect, key)` in `modelKeys.ts`; bound `sessions.ignoredLayer()` and `sessions.allowsNewHosts()` for S4's view | S4 |
| X8 | C1, `hostStandings()` | each row as the list shows it | Unchanged. Over a damaged list it gives the rows that can be read. S4 learns "damaged", "full" and "not asked yet" from `HostMemory.admission`, which S3's round sets from each admission that was not `read` (the stories document, C4; gate G2, C-4), and draws W11 | S3, S4 |
| X9 | A1-T6 | "a person's *Read* or *Forget* of that host in another window is not undone" | *Read* is not undone. *Forget* in ANOTHER window IS undone, with a notice (K5 — the parent's entry 1.1). A1-T14 gains a fourth description (an automatic row with the setting off) | The reviewer |
| X10 | C7 and A3-H1 | A3-H1 removes its rows "by S1's commit (*Forget*)" | The road's test door has `commit`, `admit`, `standings`, `stored` and `restore`; `answers` is gone. A3-H1 restores with `restore(stored())` taken before it started | S3 |
| X11 | A1-H1 | one host scenario | A1-H2 added: the setting through the real configuration | — |
| X12 | C6 | — | `dayOf(askedUtc)` is exported from `remoteHosts.ts`; the hosts list (S1) and the host sentence *allowed automatically on <day>* (S4) both call it | S4 |
| X13 | § 3, the census | 185 sites | S1 adds ONE: `remote-host-refusal-restored` (W9), so 186 after S1. The two others this spec first added, `remote-hosts-damaged` and `mend-remote-hosts`, do not exist (J3 fitted at gate G2). S4 counts from what S1 leaves | S4 |
| X14 | S3, `armFor` (gate G2, C-2) | "an eligible host = … a host whose `consentOf` is not `no`" | S3 never calls `consentOf`. A host is eligible for the timer when `sessions.hostStandings()` holds no "no" for it. Whether a host is CONTACTED is `admitHost`'s answer alone, asked before every call | S3; A1-T2's scan stays as written (`consentOf(` in `remoteHosts.ts` and in `readsKey` only) |
| X15 | C5, the state row (gate G2, C-3 and S-2) | `accountsStateOf` reads a damaged value as `{}`; `withStateChange(state, change)` takes a parsed state | S3's merge for the state row is a `Merge<C, R>` of § S1.4 — over the stored value as `unknown` — given to `sessions.commitKey`. A damaged state row reads as STOPPED, and only a person's *start* or *stop* writes over it: the rule of J-d for this row | S3; A3-T23 |
| X16 | C7, the open point below (gate G2, § 4) | — | In `ExtensionMode.Test` the round never arms itself, and the default accounts call is a stub that spawns nothing until a scenario replaces it | S3; A3-T27 |

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

**UNVERIFIED — not read in this repository, not observed.** Each is named again where the design leans on it.

| # | What | What the design does about it |
|---|---|---|
| V1 | What one `Memento.update` writes: its one key, or the extension's whole state as that window holds it. If it is the whole state, a window's write of ANY key can put back an old list of hosts | The design holds for both: A1-T3 runs against both fakes, and the repair runs after every commit and on a timer. R12 reads the result (parent `:985`: "after B's next write") |
| V2 | How soon a window sees another window's write (the parent's U12). Only an upper bound is measured (F20) | The parent's residual stands; R12 measures it |
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
2. **One function says whether a host may be contacted: `consentOf(value, host, allowNew)`.** The round's door and
   the session road both ask it, and nothing else decides. It answers `yes`, `no`, `ask` or `damaged`. A host is
   contacted only on `yes`.
3. **One function writes: `commit`.** It reads the stored value and calls the write in the same step, with no
   `await` between. It takes a change for one host. Today's two writers already read at the write (F7); what they
   lack is the merge, a cap that keeps a "no", and a seam that cannot be handed a list.
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
8. **A refusal is guarded by the window that wrote it** — before every call, after every commit, and once in 60 s
   while it holds one. A person's later "yes" releases it; nothing else does (K5, § S1.5; the parent's entry 1.1).
9. **A person's answer is stamped later than the row it replaces.** So "later" in rule 8 does not depend on two
   clocks agreeing.
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

## S1.2 — The row, old and new, and the migration

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
| Who writes a row | a person's press | a person's press; the round's door (automatic); the window that wrote a "no" (the repair) |
| What a "yes" row allows | one session read, on a press | parent § 3 (`:358-361`) |

**Migration: none on disk.** Every row stored today has no `how`, so it reads as a person's answer (parent `:324`,
appendix `:573-575`). Nothing is rewritten at activation. The first commit keeps every other row by reference, as
`withAnswer` does today (F5), so a key a later build adds to a row survives this build.

**An older build beside this one, or after a downgrade (point 6 of the stories document).** A window that was open
before the update keeps the old code until it is reloaded. That window reads an automatic row as a person's "yes"
(F2), so its session door reads without asking — also when the setting is off, which it does not know. It keeps
`how` on the rows it does not touch (F5). At its cap it drops the oldest row, a "no" among them (F5); the window that
wrote that "no" writes it again (§ S1.5), if it is open. It never guards a refusal. All of this is a residual until
the old window is reloaded (§ S1.10, residual 4). Nothing in this build depends on an old build keeping anything.

## S1.3 — Reading the store, and a list that cannot be read (K6)

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

/** Whether this host may be contacted. The ONE function that says so. A host is contacted only on 'yes'. */
export function consentOf(value: unknown, host: string, allowNew: boolean): Consent;
```

In this order: (1) a readable "no" for the host → `no`, also in a damaged list; (2) the list is damaged → `damaged`,
also for a host whose readable row is a "yes" — an entry that cannot be read may be that host's "no"; (3) a person's
"yes" → `yes`; (4) an automatic "yes" → `yes` when `allowNew`, else `ask` (parent `:377-379`: with the setting off an
automatic row counts as no answer, and it is kept); (5) no row → `ask`.

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
  looks at one of the three, nothing tells them: the cost is in § S1.10, residual 8.
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
/** A change for ONE host — never a list. `repair` is sent only by this module's own keeper; `mend` only by the change question, over a damaged list. */
export type HostChange =
  | { readonly kind: 'person'; readonly host: string; readonly answer: 'yes' | 'no' }
  | { readonly kind: 'forget'; readonly host: string }
  | { readonly kind: 'auto'; readonly host: string }
  | { readonly kind: 'repair'; readonly host: string; readonly askedUtc: string }
  /** A person's answer for one host over a damaged list: the readable rows are kept and this answer is applied, in the one write. */
  | { readonly kind: 'mend'; readonly host: string; readonly answer: 'yes' | 'no' | 'forget' };

export type Committed =
  /** `row`: the host's row after the write, or `undefined` when it was removed. `evicted`: the hosts whose row went at the cap. */
  | { readonly kind: 'written'; readonly row: HostAnswer | undefined; readonly evicted: readonly string[] }
  /** Nothing was written: the list read in this step already answers. `row` is the host's row there, if any. */
  | { readonly kind: 'kept'; readonly row: HostAnswer | undefined }
  | { readonly kind: 'full' }
  | { readonly kind: 'damaged' };

/** One commit of one key: read now, merge, write. `merge` is pure and synchronous; nothing is awaited before the write is called. */
export type Merge<C, R> = (stored: unknown, change: C) => { readonly next: { readonly value: unknown } | undefined; readonly result: R };

export async function commit<C, R>(state: KeyValueStore, key: string, change: C, merge: Merge<C, R>): Promise<R>;
```

`KeyValueStore` is the seam of `src/sideSettings.ts:11-14` (F21), imported as a type. `commit` calls `state.get(key)`,
then `merge`, then — when `next` is given — `state.update(key, next.value)`, and only then awaits. It is the commit
of the parent's rule 1 (`:333-335`) and the generic primitive of C1 and C5.

**The merge, for one host.** "Fresh" is the host's row in the list read in this step.

| The change | fresh: no row | fresh: automatic "yes" | fresh: a person's "yes" | fresh: "no" |
|---|---|---|---|---|
| `person`, "no" | write "no" | write "no" | write "no" | write "no" (a new stamp) |
| `person`, "yes" | write a person's "yes" | write a person's "yes" (no `how`) | write (a new stamp) | write a person's "yes" — a person's press changes a "no" |
| `forget` | kept, nothing to do | remove | remove | remove — a person's press |
| `auto` | **write an automatic "yes"**, by the cap rule | kept | kept | kept — never changes a "no" |
| `repair`, of a "no" written at T | write "no" | write "no" | later than T: kept, and the window lets go of it. Not later: write "no" | kept |

Every write for a host removes all rows of that host and puts one. Over a damaged list every row of this table is
`damaged`.

**`mend` is the one change that writes over a damaged list** (§ S1.3). In the one write: first the readable rows, one
for each host — the strongest, as § S1.3 reads two rows of one host — at most 100, the refusals first; then its
answer, as the `person` or the `forget` row of the table above, with the stamp and the cap below. If the answer cannot
be kept (a hundred refusals and a new host), the commit answers `full` and NOTHING is written: the list stays
damaged, and the person is told (W6). Over a list that can be read — another window mended it first — a `mend` is its
plain `person` or `forget` change. No other change, and no operation of the keeper, writes over a damaged value.

**The stamp (J4).** An automatic row is stamped `now`. A person's row, and a repaired "no", are stamped the later of
`now` and one millisecond after the `askedUtc` of the row they replace (for a repair, `now` is T). An `askedUtc` that
`Date.parse` cannot read counts as no time. "Later than T" in the `repair` row is a compare of the two parsed
instants; an `askedUtc` that cannot be parsed is never later. So an old "yes" that a stale write puts back can never
look newer than the "no" that replaced it, whatever the two clocks say.

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
place where a person's "no" is not stored; it needs a hundred refusals, and it is said to the person.

**Which test changes, and why.** `S3-T2` (`src/test/remoteRead.test.ts:170-183`) asserts today that a new host at the
cap drops the oldest row, and its fixture makes that row a "no" (`:172`, `:177`, `:179`). It is REPLACED by A1-T7,
not kept beside it: with the same fixture the "no" stays (`consentOf(next, 'oldest')` is `no`), and the row that
goes is the oldest person's "yes" (`h0`: five rows share the oldest day, and the host name breaks the tie). Kept, it
would hold the defect this story removes. `S3-T1`'s damaged half (`:162-167`) and `S3-T2b`'s two damaged lines
(`:188`, `:195`) are replaced by A1-T8 for the same reason: they assert that a damaged list is an empty one. The
summary names the three.

## S1.5 — A refusal that is repaired, and a *Forget* in another window (K5)

**This section is the parent's ledger entry 1.1** (2026-11-19, kind `approach`). The parent's § 3 says the same since
that entry: a *Forget* given in another window does not lift a "no" while the window that gave it is open.

**The keeper.** One for each window, built once in `bindRemoteSessions`. It is the bound form of this module:

```ts
export interface KeeperSeams {
  readonly state: KeyValueStore;              // the profile's globalState
  readonly key: string;                       // 'coai.terminalHosts'
  readonly now: () => string;                 // UTC ISO
  readonly allowNew: () => boolean;           // coai.terminalAccountsAllowNewHosts, read afresh at every call
  readonly changed: () => void;               // after every commit that wrote
  readonly restored: (host: string) => void;  // a refusal was written again (W9)
}                                             // no seam for a damaged list: it has no notice of its own (gate G2)

export interface HostKeeper {
  readonly stored: () => unknown;
  readonly commit: (change: HostChange) => Promise<Committed>;
  readonly commitKey: <C, R>(key: string, change: C, merge: Merge<C, R>) => Promise<R>;
  readonly admit: (host: string) => Promise<Admission>;
  readonly consent: (host: string) => Promise<Consent>;
  readonly guard: () => Promise<void>;
  readonly standings: () => readonly HostStanding[];
  readonly guarded: () => number;             // how many refusals this window guards
  readonly letGo: () => void;                 // TEST DOOR ONLY: forget every refusal this window guards (§ S1.9)
}

export function hostKeeper(seams: KeeperSeams): HostKeeper;

/** How often a window that guards a refusal looks again. Well under the round's 600 000 ms. */
export const GUARD_MS = 60_000;
```

**What a window remembers.** A map from a host to the `askedUtc` of the "no" this window wrote for it. An entry is
set when this window's commit of a person's "no" was `written`. It is removed when this window commits a person's
"yes" or a `forget` for that host — BEFORE the guard that follows the commit — and when a repair finds a person's
later "yes". It lives while the window lives (parent `:339`).

**When it looks.** `guard()` commits one `repair` for each remembered host. It runs:

1. **before every call**: first thing in `admit` (the accounts call) and in `consent` (the session read);
2. **after every commit** this window makes through the keeper — a host's change, and S3's state row through
   `commitKey`;
3. **once in `GUARD_MS`** while `guarded()` is not 0 — one `setInterval` in `remoteSessionHost.ts`, armed after a
   commit that leaves a refusal to guard, cleared at 0, disposed with the extension (J1).

A look that finds nothing missing is one read of the store and no write.

**What a repair decides** — the last row of the merge table, in the step of the write:

1. the fresh row is a "no" → nothing;
2. the fresh row is a person's "yes" stamped LATER than the remembered "no" → nothing, and the window lets go of the
   host: the person changed their mind, by a press, in some window;
3. anything else — no row, an automatic row of any time, a person's "yes" that is not later, two rows without a "no"
   → **the "no" is written again**, `restored(host)` is called, and the window remembers the new stamp.

Over a damaged list the repair writes nothing and the window keeps the host: nobody is contacted while the list is
damaged, and the first look after the person's mend writes the "no".

**How a *Forget* in another window differs from a lost "no": in the store, it does not.** Window A wrote the "no".
In window B the person presses *Forget*. B's commit removes the row, as a person's press may. A looks, finds no row,
and — rule 3 — writes the "no" again, within 60 s, with the notice W9. The person's *Forget* did not hold.

Why this and not the other way round: the two readings of "no row" are "the person lifted the refusal" and "a write
from a stale window erased it". Taking the first when the second is true contacts a host the person refused, with no
sign anywhere. Taking the second when the first is true keeps a host refused that the person wanted read, and tells
them. The second mistake is seen and has two ways out; the first is the silent failure this epic must not ship.

**What the person can do, and is told** (the change question's detail W3, and the notice W9):

- *Read* in any window lifts a refusal for good: it is a person's later "yes" (rule 2).
- *Forget* in the window that gave the "no" lifts it: that window lets go of it first.
- After the window that gave the "no" is closed or reloaded, nothing guards it, and *Forget* holds from anywhere.

**What it costs, said plainly.** With two windows open, *Forget* pressed in the window that did NOT give the "no"
is undone within a minute. If B's round runs first, B allows the host by itself and reads it once; then A's "no"
replaces B's automatic row. For E2: since entry 1.1 the parent's R8 (`:981`) says that its *Forget* is pressed in the
window that gave the host's answer, or that the record names the window and shows the notice of a refusal written
again.

**What the repair is for.** Rule 1 of the parent makes a stale VARIABLE harmless: the commit reads at the write.
It cannot make a stale WINDOW harmless: a window's read of `globalState` answers what that window holds (V1, V2).
Such a window can write a list that lacks a "no" given elsewhere a moment ago. The repair is the measure for that
case, and R12 is where it is observed. The notice W9 is kept in the notifications ledger like every notice (F19), so
E2 can count how often a "no" really went missing.

## S1.6 — The round's door and the session road

```ts
export type Admission =
  | { readonly kind: 'read'; readonly how: 'person' | 'auto'; readonly allowedNow: boolean }
  | { readonly kind: 'refused' } | { readonly kind: 'not-asked' } | { readonly kind: 'full' } | { readonly kind: 'damaged' };

export interface HostStanding {
  readonly host: string; readonly answer: 'yes' | 'no'; readonly how: 'person' | 'auto'; readonly askedUtc: string;
}
```

**`admit(host)`**, called by S3 just before EVERY accounts call, in this order:

1. `await guard()`.
2. Read the setting (`seams.allowNew()`) and the store, now, and ask `consentOf`.
3. `no` → `refused`. `damaged` → `damaged`. `yes` → `read`, with the row's
   `how`, `allowedNow: false`. `ask` with the setting off → `not-asked`; nothing is written.
4. `ask` with the setting on → commit `{ kind: 'auto', host }` and await it. `full` → `full`. `damaged` →
   `damaged`. Else read the store ONCE more, in the step that answers: `yes` → `read`, `how` from the row,
   `allowedNow` true only when this commit was `written`; `no` → `refused`.

So the marked row is in the store before S3 can spawn (parent `:838-839`), and an automatic row is never written
over a row (the merge). Over a damaged list `admit` and `consent` answer `damaged` and raise nothing: the keeper has
no seam for a notice of damage (J3, fitted at gate G2). S4 draws W11 from the admission that S3's round keeps for the
host (X8). `admit` has no way to ask a person: `KeeperSeams` has no ask.

**The session road** (`src/remoteRead.ts`). Its store seam becomes:

```ts
readonly store: {
  /** Whether this host may be read now: the guard first, then `consentOf` over the store and the setting, both read afresh. */
  readonly consent: (host: string) => PromiseLike<Consent>;
  /** The person's answer to the question, committed. */
  readonly answer: (host: string, answer: 'yes' | 'no') => PromiseLike<Committed>;
};
```

`consented` (`:366-370`): `yes` → go on; `no` → the sentence of today (`:79-80`); `damaged` → W6; `ask` → the
question. `asked` (`:356-364`): Escape writes nothing, as today; else `store.answer`; a result of `full` or `damaged`
→ its W6 sentence and no read; else as today. With the setting off, an automatic row gives `ask`, so the door asks,
and the person's answer replaces the automatic row. Nothing else of the road changes: the name re-check, the three
calls, the picked id, the sentences.

**The bound road** (`src/remoteSessionHost.ts`): `RemoteSessions` loses `answers` and gains `commitHost` (the
keeper's `commit`), `commitKey`, `admitHost`, `hostStandings`, `changeHost(host)` (the three choices for one host;
the body of today's `changed`, `:136-141`, with commits), `askHost(host)` (the question for a host with no answer,
committed), `allowsNewHosts()` and `ignoredLayer()`. Over a damaged list `changeHost` opens the change question with
W7 before its detail and commits a `mend`, and `askHost` is `changeHost` (§ S1.3). `changeHosts` stays. The change signal of today (`:165`) is the
keeper's `changed`. `setReadsKey` reads `stored()`.

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
| W3 | The change question: the detail — how the host stands, then W2, then *Forget* | One of: *Your answer now: Read, given <askedUtc>.* · *This host was allowed automatically on <day>; nobody was asked.* · *Your answer now: Not this host, given <askedUtc>.* Then W2. Then, setting on: *“Forget” removes the answer: the host then has none, and the next accounts round allows it again by itself.* Setting off: *“Forget” removes the answer: the host is then not read until you answer the question about it.* Then: *A “Not this host” is guarded by the window it was given in: after “Forget” in another window, that window writes it again. “Read” holds from every window.* |
| W4 | *CoAI: hosts read over ssh*: a row's description | a person's "yes": *the Claude session and the AI accounts are read* · automatic, setting on: *allowed automatically on <day>: the Claude session and the AI accounts are read* · automatic, setting off: *allowed automatically on <day>; not read while coai.terminalAccountsAllowNewHosts is off* · "no": *not read* |
| W4b | The same row: its detail | a person's row: *asked <askedUtc>* (as today) · automatic: *nobody was asked; written <askedUtc>* |
| W4c | The same list over a damaged list: the row of a host of a live `Creds:` terminal for which no answer can be read | *no answer can be read for this host* |
| W5 | The notice of an empty list, code `no-remote-host-asked` (today: *"…asks once"*, `remoteSessionHost.ts:113`) | setting on: *No host is in the list yet. The host of a “Creds: <host> / <session>” terminal is allowed by itself at the first accounts round and is listed here then; choose it here to forbid it.* · setting off: *No host is in the list yet. The host of a “Creds: <host> / <session>” terminal is listed here once you answer the question about it.* · the list is damaged, no answer in it can be read and no `Creds:` terminal is open: *The list of hosts read over ssh cannot be read, and no answer in it can be read. No host is read over ssh while that is so. Open a “Creds: <host> / <session>” terminal and run this again: your answer for its host repairs the list.* |
| W6 | The session door, a list that cannot be read; and an answer that could not be kept | *The list of hosts read over ssh cannot be read, so nothing was read. Run CoAI: hosts read over ssh and answer for a host there: your answer repairs the list.* · *The list of hosts read over ssh holds 100 hosts you refused, which is all it keeps, so your answer for “<host>” was not kept and nothing was read. Forget one of them in CoAI: hosts read over ssh, then answer again.* |
| W7 | The change question over a damaged list — the SAME code `change-remote-host-answer`, the same title W1 and the same three buttons: what its detail starts with, before W3 | *The list of hosts read over ssh is damaged, so no host is read by itself until it is repaired. <n> answers can still be read, <m> of them “Not this host”. <u> entries cannot be read, and one of them may have been a “Not this host”. Your answer here repairs the list: the answers that can be read are kept, and the entries that cannot are dropped. A host of a dropped entry has no answer afterwards, and with coai.terminalAccountsAllowNewHosts on it is allowed again by itself. Look through the list afterwards.* (Before gate G2 this was a question of its own, with the code `mend-remote-hosts` and one button. That code and that button do not exist.) |
| W8 | — | **Dropped at gate G2** (J3 fitted): a damaged list has no notice of its own, and the code `remote-hosts-damaged` does not exist. The number stays empty so that W9…W11 keep theirs |
| W9 | The notice of a refusal written again, NEW code `remote-host-refusal-restored`, a warning, `subject` the host | Title: *“<host>” is not read again: your “Not this host” was missing from the list of hosts, and this window wrote it again.* Cure: *If you chose Forget for it in another window: choose Read there, or Forget in this window.* |
| W10 | The accounts list, the ignored layer (S4 draws it) | § S1.7 |
| W11 | The accounts list, a host's row over a damaged list (S4 draws it) | *the list of hosts cannot be read, so this host is not read. Choose this row and answer for this host: that repairs the list.* |

The buttons *Read*, *Not this host* and *Forget*, the list's title and its placeholder do not change. No word of a
`notify(` argument in the two `remote*.ts` files is one the log scan refuses (F23): a notice is passed
`HOSTS_SAID.<name>(host)`, and no variable there is called `rows`, `names` or `reason`.

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
  /** The stored value as it is, for the `finally`. */
  readonly stored: () => unknown;
  /** TEST ONLY. Puts a stored value back whole and makes this window let go of every refusal it guards. */
  readonly restore: (value: unknown) => Promise<void>;
}

export const testDoorOf = (sessions: RemoteSessions, state: vscode.Memento): RemoteSessionsTestDoor => …
```

- `restore` is the ONE place of the extension that writes a whole value under `coai.terminalHosts`. It is in
  `testDoorOf`, which `extension.ts` calls only behind `ExtensionMode.Test` (`:935`; the guard test's pattern
  `remoteSessions\s*:\s*testDoorOf\(` still fits a second argument, `remoteExportGuard.test.ts:17`).
- Why not restore by commits: a commit cannot put back an automatic row or an old `askedUtc`, so the profile would
  not be as it was found. Why it must also empty the window's memory: a scenario that committed a "no" would
  otherwise have it written again by the guard, after the scenario ended.
- `letGo` is the keeper's one lever that weakens a refusal, so it is kept out of reach: the keeper is held in the
  module, as `bound` is today (`src/remoteSessionHost.ts:196`); `RemoteSessions` does not hand `letGo` out; and
  A1-T18 holds that `letGo(` is called in `testDoorOf` and nowhere else.
- Why set-up is by commits: the scenarios then go the road a press goes, merge and cap included.

**The scenarios that change.** `terminalHostsScenario.ts`: `:55` `before = door.stored()`; `:59` and `:67` become
`door.commit({ kind: 'person', host: HOST, answer: … })`; `:60` and `:68-69` read `door.standings()`; `:87`
`door.restore(before)`. `terminalDoorsScenario.ts`: the same at `:59`, `:63`, `:73`, `:75`, `:78`, `:87`, `:96`,
`:112`; its two `withAnswer([], HOST, 'yes', …)` (`:63`, `:96`) replaced the WHOLE list by one row, and are now one
commit that leaves the other rows alone. The imports of `withAnswer`, `answersOf` and `consentOf` go.

## S1.10 — The security judgement

Decided before any S1 code. Each item says the decision, what it stands on, and what was NOT observed. Where an
effect is VS Code's and not ours, the words are "we declare" and "we read", not "it protects".

**What this story guards.** Since the owner's decisions O3, N2 and Q1 the store no longer stands before a host is
contacted (parent, finding F1, `:659-672`). What is left of a gate is the refusal. This story makes that one hold,
and it makes the automatic "yes" something only the person can take back.

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
a store of a hundred person's rows answers `full` (A1-T7). The cost is noise: up to 100 rows, about 12 KB, and one
notice a day that names them.

One thing a name gains beyond the login, with the setting on: on that host the session door no longer asks (parent
`:353-356`). A session read still needs a person's press in that terminal.

With the setting off, a name gains one thing: the host appears in the list as *not asked yet*, and a person may
press it. The question then names the host and what a "yes" covers (W1, W2).

**An attacker that is another extension, beyond naming a terminal.** It cannot reach the store: `globalState` is this
extension's own; the test door is not an export outside test mode (F11); and no registered command takes a host or
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
- **J-b. An automatic "yes" is written only over no row, in the step of the write.** So a refusal that this window
  can see is never replaced, whatever was read before an `await`. *Not observed:* a refusal this window cannot see
  yet (V2). That is the parent's residual, and the repair's job.
- **J-c. A missing "no" is written again by the window that wrote it, and "no row" counts as missing (K5; the
  parent's entry 1.1).** It fails closed. *Not observed:* whether a "no" ever goes missing on a real machine (V1, V2). The measure is therefore
  described as what the window DOES — it looks, and it writes — not as "a refusal cannot be lost". It can: when the
  window that wrote it is closed, nobody looks.
- **J-d. A list that cannot be read contacts nobody and is never written over (K6).** The alternative that reads
  what it can is one unreadable entry away from allowing a refused host. It is mended only by a person's own answer
  for a host, and it has no notice and no question of its own (gate G2): so the damage is said where a person looks
  — a host's row, the session door, the hosts list — and nowhere else.
- **J-e. The cap drops no refusal (K7).** Finding 6 of the gate round gave the installer's record the same rule: a
  row that goes takes its guard with it (appendix `:380-383`).
- **J-f. A marker this build does not know makes the list unreadable.** It is the same rule as J-d, applied to one
  row.
- **J-g. Only the user layer decides the setting, by the read and by the declared scope.** Two layers, and the read
  is the one we can test.
- **J-h. With the setting off, an automatic row is not a "yes" for the session door either.** One row means one
  thing for both readers (appendix `:592-596`); a second meaning would need `consentOf` to know who asks.
- **J-i. The door of the round cannot ask.** A timer that could raise a modal would train a person to press it.

**Residuals, named.**

1. **One more call before a refusal is seen.** Until this window can see a "no" given in another window, its round
   can call that host once more (parent `:343-346`, `:712-713`). If its write then erases the "no", the window that
   gave it writes it again within 60 s — if that window is open.
2. **A refusal is guarded only while the window that gave it lives.** After a close or a reload the row is in the
   store and nobody re-writes it.
3. **A *Forget* in another window does not hold** while the window that gave the "no" is open (K5; the parent's
   entry 1.1). Said in the question and in a notice.
4. **A window still running an older build** reads an automatic row as a person's "yes", also with the setting off;
   drops the oldest row at its cap, a "no" among them; and guards nothing (§ S1.2). Until it is reloaded.
5. **A hundred refusals.** The next person's answer is not kept. Said to the person; that host is not contacted by
   the round while the list is full.
6. **A press in flight** is not stopped by a "no" given during it: at most three calls, 15 s each, as today (F7).
7. **The setting can be written by other code in the editor** (V4), and Settings Sync may carry it to the person's
   other machines (V3). It is still the person's own layer.
8. **A damaged list** stops every accounts read until a person repairs it, and the repair can drop a refusal that
   could not be read. The question says so. **No notice says that the list is damaged** (gate G2): a person learns
   it from a host's row of the accounts list, from the session door or from *CoAI: hosts read over ssh*. Until they
   look, every host is not read and nothing has told them why. It fails closed, and silently.
9. **Two clocks.** A person's *Read* in a window whose clock is behind AND whose view is stale can be taken for an
   old row and undone. It fails closed, with the notice.
10. **With the setting on, the first contact comes before any telling.** That is F1 and the owner's decision; this
    story does not change it.

**`SECURITY.md`** is S5's (stories document, S5 item 5). From this spec it takes: the merge and the cap; that a
window re-writes a refusal it gave, and when; that *Forget* from another window does not hold; that a damaged list
contacts nobody; the setting, with "we declare" for the scope and "we read" for the user layer; residuals 1–9.

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
every key as this view holds it — V1's worse case). A1-T3 and A1-T6 run in both modes. Every view marks the step:
`get` sets a flag and clears it in a microtask; `update` records whether the flag was set.

Files: **RH** = `src/test/remoteHosts.test.ts` (new, pure). **RR** = `src/test/remoteRead.test.ts` (changed).
**AS** = `src/test/terminalAccountsSetting.test.ts` (new). **HW** = `src/test/remoteHostsWiring.test.ts` (new; source
scans of host code, each with a companion that the scan finds what it looks for).

| # | Name (file) | Input → expected | RED today |
|---|---|---|---|
| A1-T1 | a row says how it came, and a marker nobody knows is not read (RH) | A row with `how: 'auto'` → its standing is `auto`. A row with no `how` → `person`. The four row shapes 0.89.1 writes (a "yes", a "no", any string as `askedUtc`, an extra key) → `person`, and after a commit for ANOTHER host each is the same object. `how: 'later'`, and `how: 'auto'` beside a "no" → `consentOf` is `damaged`. `undefined` and `[]` → no standings, `ask` | *a row with a marker this build does not know was read as a person's "yes"* (F2) |
| A1-T2 | one function decides, and both doors ask it (RH, HW) | The table of § S1.3 over 12 stores × the two setting values: `consentOf` gives the listed word. For the same 24 cases `keeper.consent` gives the same word, and `keeper.admit` gives `read` exactly where it is `yes` or where `ask` meets the setting on. Two rows of one host, one of them a "no" → `no`. HW: `TERMINAL_HOSTS_KEY` is named in `remoteSessionHost.ts` only, in the keeper's seams and in `testDoorOf`; no `.get(` or `.update(` of any source file is given the literal `'coai.terminalHosts'`; `consentOf(` is called in `remoteHosts.ts` and in `readsKey` only; companions: each scan finds its sanctioned sites | *an automatic row counted as an answer with the setting off*; the scan: *a second writer of coai.terminalHosts: `changed` in remoteSessionHost.ts* |
| A1-T3 | a "no" against an automatic "yes", in both orders, with a stale view (RH) | Windows A and B over one backing. **(a) auto, then no:** B admits `h` (an automatic row, `allowedNow` true); A commits "no" → the backing holds ONE row for `h`, the "no"; B syncs → B's `admit` is `refused`. **(b) no, then auto, B's view fresh:** A commits "no"; B syncs; B admits → `refused`, 0 updates by B. **(c) no, then auto, B holding a list from before A's write:** A commits "no"; B, not synced, admits → B's ONE admission is `read` (the one call the parent's U12 names) and the backing has lost the "no"; A syncs and guards → the backing holds the "no" again, `restored('h')` once; B syncs → `refused`, and from then on 0 admissions `read`. **(d) the same loss by another key, `'whole-state'` only:** B, stale, commits the state row through `commitKey` → the backing's list is B's old one; A's guard writes the "no" again. **(e) two refusals, each window stale to the other:** A refused `h`, B refused `g`; each writes over the other; after one sync and one guard each, the backing holds both | (c): *the store lost “Not this host” for h and nothing wrote it again*; (a), with a stub that writes "yes" over any row: *an automatic “yes” replaced a “Not this host”* |
| A1-T4 | who may change which row (RH) | Every cell of the merge table of § S1.4: the row after, and whether an update happened. A person's "yes" over an automatic row has no `how`. `auto` over any row → `kept`, 0 updates. `auto` and `repair` over a "no" → the same "no" object. `forget` over no row → `kept`, 0 updates | *an automatic “yes” replaced a row* |
| A1-T5 | no list is ever handed to a write (RH, RR, HW) | Over every operation of the keeper (each change kind, `admit`, `guard`, `mend`, `commitKey`): every `update` was called in the step of a `get` of the same key. Type level: an object literal with `write` is not a `RoadSeams['store']` (`@ts-expect-error`). HW: no source file exports `withAnswer` or `withoutHost`; `state.update(` is given the hosts key in the keeper's seams and in `testDoorOf` only | The step half is GREEN today — both writers read at the write (F7). RED today is the seam: *RoadSeams.store takes a list to write* |
| A1-T6 | a refusal this window gave is written again, and a person's later press is not undone (RH) | A commits "no" for `h` at T. **(a) lost:** the backing's row is removed behind A's back; each of the three moments alone writes it again — `admit('other')`, a commit for another host, and a bare `guard()` (the timer's call); `restored('h')` once each. **(b) *Read* in B:** B commits a person's "yes" → A's guard writes nothing and lets go; a later loss of that row is NOT repaired by A. **(c) *Forget* in B:** B commits `forget` → A's guard writes the "no" again and calls `restored('h')` (K5; the parent's entry 1.1). **(d) *Forget* in A:** A commits `forget` → the row is gone, 0 repairs follow, and A's `admit('h')` with the setting on writes an automatic row. **(e) an old "yes" put back:** a person's "yes" stamped before T replaces the "no" → written again. **(f) two clocks:** B's clock is one hour behind; B, synced, commits a person's "yes" → its stamp is later than T and A lets go; B, stale, does the same → A writes the "no" again. **(g) a damaged list:** the backing becomes `'junk'` → A's guard writes nothing and still guards; after a `mend` (a person's answer for ANOTHER host), the next guard writes the "no" | *a “Not this host” that went missing was not written again* |
| A1-T7 | the cap drops no refusal, and no person's row for an automatic one (RH) | **The fixture of S3-T2** (99 person's "yes", one "no" that is the oldest row) + a person's "yes" for a new host → 100 rows; `oldest` is still `no`; `h0` is gone. 99 automatic + 1 "no" + an `auto` commit for a new host → `written`, the oldest automatic row went and `evicted` names it; `admit` of one more new host → `read`. 100 person's rows + `admit(new)` → `full`, 0 updates, and the store is the same value. One "no" + 300 admissions of invented hosts → the "no" holds, 100 rows. 100 refusals + a person's "no" or "yes" → `full`; through the road → W6's second sentence, 0 runs. A changed answer of a known host at the cap drops nothing | *the list dropped a “Not this host” to make room* — what `:177` and `:179` of today's test assert |
| A1-T8 | a list that cannot be read allows nobody (RH, RR) | For each of `'junk'`, `null`, `7`, `{}`, `[null]`, `['h']`, `[{ host: 'h' }]`, `[yes for z, 'junk']`, `[no for x, { host: 'y', answer: 'maybe', askedUtc }]`, `[yes with how 'later']`: `admit` of a new host, setting on → `damaged`; `admit('x')` → `refused`; `admit('z')` → `damaged`; every change kind but `mend` → `damaged`; 0 updates in all, and the stored value is the same value; **no notice is raised: `KeeperSeams` has no member for one** (an object literal with `damaged` is not a `KeeperSeams`, `@ts-expect-error`). 100 admissions, 100 guards and a `commitKey` of another key leave the damaged value the same value. Through the road: W6's first sentence, 0 asks, 0 runs. **The mend is a person's answer (gate G2):** `{ kind: 'mend', host: 'q', answer: 'no' }` over `[yes for z, 'junk', no for x]` → ONE update; the stored value is `x`'s "no" (the same object), a "no" for `q` and `z`'s "yes", the refusals first; then `admit` works again. The same with `answer: 'yes'`, and with `'forget'` for `z` (its row is gone). Over 100 readable refusals and one bad entry: a `mend` for a NEW host → `full`, 0 updates, the same value; a `mend` with `forget` for one of the hundred → written. Over a list that can be read, a `mend` is its plain change. **Reaching the question:** `listedHosts` over `[yes for z, 'junk']` with the live hosts `z` and `q` → `z` with its answer and `q` with W4c, each once; over a list that can be read the live hosts are not added; over `'junk'` with no live host → no row, and the command says the third wording of W5. **One positive with the same fixture shape:** `[yes for z]`, not damaged → `admit('z')` is `read` | *a damaged list let a new host be allowed by itself*; and through the road: *a damaged list was asked about and then written over: 1 row left of 2* |
| A1-T9 | the store and the setting are read again before every call (RH) | Two admissions of `h` with a person's "yes": between them another view commits "no" and this one syncs → the second is `refused`. Two admissions of two new hosts: the `allowNew` seam turns false between → the first writes a row, the second is `not-asked` with 0 updates. `allowNew` was called at least once for each admission, after its guard | *a “no” given between two calls did not stop the second* |
| A1-T10 | the setting, on and off (RH, RR) | **On:** `admit` of a host with no row → `read`, `auto`, `allowedNow` true; again → `allowedNow` false. With an `update` that resolves only when the test lets it, the admission does not resolve before it: the row is stored BEFORE a call can be spawned. **Off:** no row → `not-asked`, 0 updates; an automatic row → `not-asked`, and the row is still there; a person's "yes" → `read`. **On again:** the automatic row → `read`, `auto`, `allowedNow` false | *an automatic row counted as an answer with the setting off*; *the host was answered “read” before its row was stored* |
| A1-T11 | only the user layer decides, and nothing carries the setting in (AS; `modelKeys.test.ts`; `modelKeysAreReadOnce.test.ts`; `helpCoverage.test.ts`) | `allowNewHostsFrom(userLayerReader(merged, inspector(layers))(KEY))` for the mixes: nothing set → `true`; user `false`, workspace `true` → `false`; user `true`, workspace `false` → `true`; user unset, workspace `false` → `true`; user unset, folder `false` → `true`. User `'true'`, `0`, `{}` → `false`; `null` → `true`. `ignoredLayerOf` → `workspace`, `folder` (folder first when both), `undefined`. The manifest: boolean, default `true`, `"scope": "application"`; the description names the default, "asked first" and the user setting. `SAFETY_KEYS` deep-equals the two keys (`modelKeys.test.ts:67-73`, its name reworded). The direct-read scan matches `config.get('terminalAccountsAllowNewHosts')` (one more planted line, `modelKeysAreReadOnce.test.ts:45-50`). `NEVER_TRANSFERRED` holds the key with a reason of more than 20 characters; `importedConfig` of a file that carries it → refused with `never transferred:`; `exportedSettings` with a base value `false` → not exported. The key is not in `OVERLAID_SETTINGS` and not a key of `DEFAULTS`. HW: the body of `allowsNewHosts` in `sideConfig.ts` calls `userLayer(` and not `readerFor(`; the keeper's `allowNew` seam holds `getConfiguration('coai')` inside its arrow. `SETTING_ALIAS` has the key, its phrase is in the English help, and each of the five chat articles holds `` `coai.terminalAccountsAllowNewHosts` `` | *coai.terminalAccountsAllowNewHosts is not in package.json* |
| A1-T12 | the session doors (RR) | `remoteRoad` over a keeper and a fake store. Setting on, an automatic row → 0 asks, 1 run. No row → 1 ask. Setting off, an automatic row → 1 ask; *Read* → the row is a person's (no `how`) and 1 run; *Not this host* → "no", 0 runs; Escape → the row is still the automatic one, 0 updates, 0 runs. A person's "yes", setting off → 0 asks, 1 run | *an automatically allowed host was read without a question with the setting off* (F2) |
| A1-T13 | *Forget*, and *Not this host* on an automatic row (RH) | An automatic row; `forget` → no row; `admit` (on) → an automatic row again, `allowedNow` true. With the setting off → `not-asked`. A person's "no" over an automatic row → one row, no `how`; `admit` → `refused` | *Forget left the host allowed* |
| A1-T14 | the words (RH, HW) | Each of W1–W7, W9 and W11 (W4c and the three wordings of W5 among them; there is no W8) equals its text of § S1.8, for a host `h` and both setting values where it has two. The question keeps its code `read-remote-session` and its two buttons (HW: both are literals of the host file, and `notification-sites.json` holds the code). W5 does not hold `asks once`. `dayOf` of an instant built from this machine's LOCAL noon gives that local day; of `'x'` → *an unknown day*. HW: `remoteSessionHost.ts` holds no `Read the Claude session on` literal; each of the four codes (`read-remote-session`, `change-remote-host-answer`, `no-remote-host-asked`, `remote-host-refusal-restored`) is a string literal there, once; and NO source file holds `mend-remote-hosts` or `remote-hosts-damaged` — a damaged list has no code of its own (gate G2) | *the question still says “Read the Claude session on …”*; *the empty list still says the first read asks once* |
| A1-T15 | the door of the round never asks (RH) | `KeeperSeams` has no member whose name holds `ask` (an object literal with `ask` is not one, `@ts-expect-error`). 100 admissions of new hosts, setting on and off, over a keeper whose road-side ask throws → nothing is raised | with a stub that asks on `ask`: *the door of the round asked a person* |
| A1-T16 | the commit is generic (RH) | `commit(view, 'coai.terminalAccountsState', { stop: T }, merge)`: the value was changed after the caller last read it → `merge` is given the value of NOW; the step rule holds; a `merge` that gives no `next` → 0 updates. `keeper.commitKey` runs the guard after its write: a refusal lost before it is back after it | *the state row was written from a value read earlier* |
| A1-T17 | a person's answer is stamped after the row it replaces (RH) | A "yes" stamped one hour in the future; a person's "no" at `now` → its `askedUtc` is that stamp plus 1 ms. A row whose `askedUtc` is `'x'` → the new stamp is `now`. An automatic row → `now` | *an older “yes” looked newer than the “no” that replaced it* |
| A1-T18 | the host half is wired as written (HW; `remoteExportGuard.test.ts`) | `remoteSessionHost.ts` holds ONE `setInterval(`, its callback calls `guard(`, its delay is `GUARD_MS`, and its handle is cleared and disposed; `GUARD_MS` is 60 000. The handler of `registerCommand('coai.terminalHosts'` takes no parameter. `testDoorOf(` is built once, behind the test-mode check, with the `Memento` as its second argument; `restore` is a member of the test door only, and `letGo(` is called in `testDoorOf` only. `kind: 'mend'` is written in `remoteSessionHost.ts` in ONE place, inside the change question's handler. `notification-sites.json` holds the ONE new code, `remote-host-refusal-restored`, and `PLACES_THIS_SPEAKS` is the counted number (186) | *no timer looks again in a window that makes no call* |
| A1-H1 | the store in a real editor (`terminalHostsScenario.ts`, extended; CI only) | `before = door.stored()`. `door.admit('h1')` with the default setting → `read`, `auto`; `door.stored()` — a fresh `get` — holds a row for `h1` with `how: 'auto'`. `door.commit` a person's "no" for `h1` → `door.admit('h1')` is `refused`, and the stored row has no `how`. In a `finally`: `door.restore(before)`, and `door.stored()` deep-equals `before` | cannot be written today: the door has no `admit` |
| A1-H2 | the setting through the real configuration (the same file; CI only) | `getConfiguration('coai').update('terminalAccountsAllowNewHosts', false, Global)` → `door.admit('h2')` is `not-asked`, and no row for `h2` is stored. Back to `undefined` in a `finally` → `door.admit('h2')` is `read`; then `restore`. It records what `inspect()` answers for the key. If the editor refuses the update, the scenario FAILS and says so (V5) | — |

**What these do not prove.** Two real windows (V1, V2: R12). What VS Code does with an `application` key in a
workspace or a remote window (V3: R11). An older build beside this one. The modal as a person sees it (V6).

**Mapping to the stories document.** Every id A1-T1…T16 and A1-H1 is kept with its meaning; A1-T6 and A1-T14 are
refined as X9 says. A1-T17, A1-T18 and A1-H2 are added.

## S1.12 — Break-its (a PRODUCTION change; each seen RED with its message, then restored)

| # | Change | Goes red |
|---|---|---|
| B1 | The row check ignores `how` | A1-T1 — *a row with a marker this build does not know was read as a person's "yes"* |
| B2 | `admit` looks at the rows itself and not at `consentOf` | A1-T2 — *the round read a host that counts as not answered* |
| B3 | An `auto` change replaces a row | A1-T3 (a), A1-T4 — *an automatic “yes” replaced a “Not this host”* |
| B4 | No guard after a commit | A1-T3 (c) — *the “no” this window wrote stayed lost after its next commit* |
| B5 | `commitKey` writes without the guard | A1-T3 (d), A1-T16 — *a refusal lost by a write of the state row was not written again* |
| B6 | `await Promise.resolve()` between the read and the write in `commit` | A1-T5 — *the store was written in a later step than it was read* |
| B7 | A repair writes over a person's later "yes" | A1-T6 (b) — *a person's Read in another window was undone* |
| B8 | The window lets go of a refusal when it finds no row | A1-T6 (a), (c) — *a “Not this host” that went missing was not written again* |
| B9 | The guard runs before the window lets go of a host it forgot itself | A1-T6 (d) — *Forget in the window that refused the host was undone* |
| B10 | The repair compares `askedUtc` as text, and a person's answer is stamped `now` | A1-T6 (e), A1-T17 — *an older “yes” looked newer than the “no” that replaced it* |
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
| B25 | The `setInterval` is removed | A1-T18 — *no timer looks again in a window that makes no call* |
| B26 | `testDoorOf` is built outside the test-mode check | `remoteExportGuard.test.ts` — its message of today |
| B27 | `restore` does not empty the window's memory | A1-H1 — *the profile was not left as it was found* (CI) |
| B28 | `admit` commits a `mend` when it meets a damaged list | A1-T8 — *a damaged list was repaired with no press* |
| B29 | A `mend` keeps the readable rows and drops the person's answer; then it puts the answer first and cuts at 100 | A1-T8 — *the answer that repaired the list was not kept*; *a mend dropped a “Not this host”* |

## S1.13 — The brief: order, files, commands

One commit for each step; `npm run compile` exit 0 and the whole suite green at each.

1. **Move.** NEW `src/remoteHosts.ts` with today's consent section (`src/remoteRead.ts:28-38`, `:112-147`, `:160-166`) moved
   unchanged; `remoteRead.ts` imports what it uses. No behaviour changes; every test of today is green.
2. **Read.** `hostsOf` (private), the row check with `how`, `consentOf` with `allowNew` and `damaged`, `standingsOf`,
   `dayOf`. A1-T1, the table half of A1-T2, the read half of A1-T8. `S3-T1`'s damaged half and `S3-T2b`'s two lines
   are replaced.
3. **Write.** `commit`, the private merge, the stamp, the cap. A1-T3 (a) and (b), T4, T5, T7, T16, T17. `S3-T2` is
   replaced by A1-T7. `withAnswer` and `withoutHost` are deleted here.
4. **Keep.** `hostKeeper`: the memory, `guard`, `admit`, `consent`, `commitKey`, `mend`. A1-T3 (c)–(e), T6, the
   rest of T8, T9, T10, T13, T15.
5. **The road.** `RoadSeams.store` in `remoteRead.ts`; the two W6 sentences; `readsKey`. The `world()` of
   `remoteRead.test.ts` (`:107-121`) is built over a keeper and a view. A1-T12.
6. **The setting.** `modelKeys.ts`, `sideConfig.ts`, `package.json`, `configTransfer.ts`, the help sentence in five
   languages, `SETTING_ALIAS`. A1-T11.
7. **The host.** `remoteSessionHost.ts`: the keeper bound; the words; the list's rows — over a damaged list the
   readable answers and the hosts of the live `Creds:` terminals, through `liveTerminals` and `credsSession`, which
   exist; the change question, which over a damaged list starts with W7 and commits a `mend`; the ONE notice (W9);
   the timer; `changeHost`, `askHost`; the test door. `extension.ts:935`: `testDoorOf(remote,
   context.globalState)`. `node scripts/count-notifications.mjs --write`, and `PLACES_THIS_SPEAKS` raised by one, to
   186, with a dated comment that names the code `remote-host-refusal-restored`. A1-T14, T18. Keep `liveNameOf` as it
   is (F26).
8. **The host scenarios.** § S1.9; A1-H1 and A1-H2. They run only in CI's real-editor job; its result is read from
   that job on the head commit.

Lint: `complexity` 4 and 50 lines for a function. The merge is one small function for each change kind behind a
table of the kinds, as `FOLLOW` is in `remoteRead.ts:415-429`; `admit` is the four steps of § S1.6 as four functions.
No suppression is added.

Commands, from `src_vs_code/`: `npm ci`; `npm run compile` (read the exit code); `node
../.agents/conventions/tools/low-priority.mjs node scripts/run-tests.mjs out/test/remoteHosts.test.js`; `node
../.agents/conventions/tools/low-priority.mjs npm test`; `npm run lint`. From the worktree root: `node
.agents/conventions/tools/plan-lifecycle.mjs`.

## S1.14 — Growth

- **The store**: at most 100 rows, about 120 bytes each, about 12 KB. Automatic rows push each other out; a person's
  "yes" goes only for a person's newer answer; a "no" goes only by a person's press.
- **A window's memory of its refusals**: one entry for each host the person refused in that window and did not
  allow again; it goes with the window.
- **The timer of the guard**: one for each window, only while that window guards a refusal; one read of the store
  each minute; cleared at 0 and disposed with the extension.
- **Notices**: one for each refusal written again — each needs a person's press elsewhere or a real loss. None for a
  damaged list. All go through the one door, which keys repeats by code and subject (F19).
- **Interrupted**: a window that closes in the middle of a commit leaves the old value or the new one, never half of
  a list; nothing here has a state between.

## S1.15 — What the Opus reviewer checks

1. No function of `src/` outside `testDoorOf` writes a value under `coai.terminalHosts` that was not built from a
   read in the same step — by reading `commit`, not by the test alone.
2. The merge table of § S1.4, cell by cell, against the code; and that `repair` is sent only by the keeper, and
   `mend` only by the three answers of the change question over a damaged list.
3. The order in the keeper's commit: the window lets go of a host it forgot or allowed itself BEFORE the guard runs.
4. The cap never drops a "no": every path that shortens the list.
5. A damaged value: every door, and that no commit but a person's `mend` writes over it. No notice, no question and
   no code of its own exists for a damaged list (gate G2).
6. The setting is read through `userLayer`, afresh, and through no overlay; a NEW configuration object at each call.
7. The three replaced tests are gone, not kept beside A1-T7 and A1-T8.
8. Every break-it was seen red with the message of § S1.12.
9. The rows X1–X16 are in the stories document and in the S3 and S4 briefs.
10. No sentence of a doc comment says what VS Code does with the scope, or that a refusal "cannot" be lost.

## Definition of Done — E1.S1

- [ ] The caller read "Changes to the parent" and J1–J6 BEFORE any S1 code, and recorded in the parent's ledger any
      item judged a change. (Gate G2, 2026-11-19: K5 with J2 is entry 1.1; J3 is fitted; the rest is inside.)
- [ ] This spec was reviewed by Opus (gate G2), and X1–X16 are in the stories document and in the S3 and S4 briefs,
      in one commit.
- [ ] A1-T1…T18 green; each RED and each break-it B1…B29 observed with its message; all in the summary, with the
      three replaced tests named (`S3-T1`'s damaged half, `S3-T2`, `S3-T2b`'s two lines).
- [ ] `npm run compile` exit 0; the whole suite and lint green; no suppression count grew;
      `notification-sites.json` generated again.
- [ ] A1-H1, A1-H2 and the two rewritten scenarios green in CI's `extension · a real editor` job on the head commit,
      read from that job.
- [ ] The help sentence is in five languages; the rest of the documents are S5's.
- [ ] `node .agents/conventions/tools/plan-lifecycle.mjs` clean.
