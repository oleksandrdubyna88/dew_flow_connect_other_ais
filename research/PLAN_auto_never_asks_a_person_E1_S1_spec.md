# PLAN — E1.S1 spec: the stored policy and the hold's authority — `autonomous` on disk, and the one hold `auto` may release (CORNERSTONE)

> Status: **IMPLEMENTED, 2026-11-19** — the E1.S1 cornerstone spec, built in #1011 (mcp 0.66.0); promoted with its plan. Before that: **spec only, revised to epoch 2 of the parent on 2026-11-16, by Fable, before its code — nothing coded.** U1
> is settled: it became the parent's entry 1.2, and the revised § 4.3 fixes the released state (`AwaitingResolve = true`,
> `AdvanceOnResolve = true`). U2 and U3, reported from this spec, are settled in the parent the same day, neither
> counted as a change (§ S1.8): the release note reaches `coai.db` by a one-column update, and an answer found at the
> second read gets its withdrawn cards restored. Nothing here waits for a word.
> Cornerstone spec of story E1.S1 of [PLAN_auto_never_asks_a_person_E1.md](PLAN_auto_never_asks_a_person_E1.md) (its § 7;
> parent [PLAN_auto_never_asks_a_person.md](PLAN_auto_never_asks_a_person.md), § 3 requirements 4, 7, 8 and 9, § 4.1,
> § 4.3, the first row of § 4.4, § 6, § 8 steps 2, 3, 4 and 6, § 13 item 4, note N2). It holds the security judgement on
> who may release a hold (§ S1.7), which must stand before any S1 code. It keeps no change ledger: a change is an entry
> in the parent. It is promoted with the E1 document.
>
> Scope: `src_mcp` (core, server, store, tests) and `src_bench`. NEW `src_mcp/core/Rounds/LegacyHold.cs`,
> `src_mcp/src/Server/Rounds/SessionPolicy.cs`, `src_mcp/src/Store/RoundNotes.cs`; changed `SessionState.cs`
> (`PanelConfig`), `PanelSettings.cs` (`PolicyOf`, `PolicyUnread`), `SessionStore.cs:704`, `Escalations.cs` (`Withdraw`),
> `SettingsRead.cs` and `SettingsFile.cs` (a read that failed is told apart), `PanelServiceHost.cs` (such a read is read
> again), `StartupNotices.cs` (one method), five call sites, `SettingsApplied.cs`, tests, `research/module_tests.md`.
> No verdict, no `ask_human` reply, no tool text, no `--features` entry, nothing under `src_vs_code`. New words an AI or
> a person reads, and no others: two refusal sentences of a review entry (§ S1.3) and one startup note (§ S1.5.2).
> Coder: Sonnet. Review: Opus. Read at `1f0ae332` (code identical to `origin/main` 4cb32a76); read again at `47550a7c`
> for this revision — the four code files that differ since are the agy hook's, and none is named here. Paths are from
> the repository root.

## Changes to § 4 of the stories document (made in the same working tree)

| # | Contract | § 4 said | This spec says | Why | Who must follow |
|---|---|---|---|---|---|
| X1 | C1 | `bool? Autonomous { get; init; }` | `Autonomous` and `OnExhausted` have a `private init`. Two constructors (code, file). New read-only `PredatesAuto`, and `UnderPolicyOf(current)` — the only way to change the pair | The pair cannot be written half, and the marker cannot be made absent, outside the type (§ S1.2) | S2 and S3 still read only `IsAutonomous` |
| X2 | C2 | an auto config in a test is `Uniform(max, threshold, GoodEnough) with { Autonomous = true }` | it is `PanelConfig.Uniform(max, threshold)` — no policy named is policy auto. The `with` form does not compile | X1 | S2 § 8.2 helper `Auto`, S3 § 9.2 row 6 — both edited |
| X3 | C4, the name | "the parent's `CurrentPolicy`" | `SessionPolicy.Current(session)` for a caller that holds the claim; `SessionPolicy.CurrentUnclaimed(session, repoPath, at)` for one that does not; the begin calls the two halves (`Released`, then `LegacyHold.Stamped` at `:279`) | § S1.5 | S3 reads the `Release` S1 already wired at `AskHumanService.cs:100` (X8) |
| X4 | C4, `HeldSentence` | `HeldSentence` passes the session through it | its one caller does (`PanelService.WhyNotAFreshReview`, `:1152-1158`), before the `switch` | `HeldSentence` is only reached for a held session and returns a sentence; after a release there is none | — |
| X5 | C4, F0 and F1 (plan round on the stories document, accepted by the coordinator) | the marker is stamped on every deciding load; the notice is withdrawn | while a hold from before `auto` stands its config is not stamped; the notices are withdrawn first and the session saved second; a notice that could not be withdrawn keeps the hold | § S1.1 D8, D9 | — |
| X6 | C4, its last sentence; S3 § 9.2 | `autonomous = session?.State.Config.IsAutonomous ?? _settings.Rounds.IsAutonomous` | `autonomous = _settings.Rounds.IsAutonomous` | Under X5 a held session can have `IsAutonomous == false` while the settings say `auto` (a release that could not be written). Read from the session, `ask_human` would write a card and wait | S3 — the line is edited |

**Added for epoch 2 of the parent (2026-11-16).** § 4 of the stories document is edited to these in the same working tree.

| # | Contract | Epoch 1 said | This spec says | Why | Who must follow |
|---|---|---|---|---|---|
| X7 | C4, the released state | `AwaitingResolve` kept; U1 open | `AwaitingResolve = true` and `AdvanceOnResolve = true`, always | the parent's entry 1.2 and § 4.3 | — |
| X8 | C4, what a deciding load returns | a session | `Released`, `Current` and `CurrentUnclaimed` answer a `Release(Session, NotWritten)`: the session to go on with, and — for a hold from before `auto` that could not be released this time — why. A caller that only needs the session reads `.Session` | a review entry must say the reason (parent § 4.3, *What a review entry answers*) | S3: S1's line at `AskHumanService.cs:100` keeps the whole `Release` in a local, and S3 reads its `NotWritten` — not empty is the hold whose release could not be written; empty on a held session is the person's hold. `LegacyHold.Stands` does not tell the two apart: a hold from before `auto` that the person answered stands too. Among the person's holds it only picks the words (N3's three rows) |
| X9 | C4, the release | notices first, the session second | also: the person's answer on file wins and is read again after the withdrawal; the release is noted on the held round, in the file and in `coai.db`; a review entry has two sentences of its own and never answers `GateHeld` for such a hold | parent § 4.3 as revised | S3 words `ask_human`'s reply for the same two cases (parent § 4.4, first row) |
| X10 | C3 | — | `PanelSettings.PolicyUnread`: the policy is a default taken because the settings file could not be read. Rounds run on it; no hold is released on it | parent § 6, *A failed read authorises no release* | S2 and S3 do not read it. The note for a failed read is S1's (D15); the pinned-policy note stays S3's |

## S1.0 — What was read and what was measured

Read at `1f0ae332`.

| # | Fact | Where |
|---|---|---|
| F1 | `PanelConfig` is a positional record `(Roles = null, OnExhausted = StagePolicy.Human)`; `Roles` is declared again with `= Roles ?? Defaults()`; `Catalog` is `[JsonIgnore]`; `SecurityLane` has an initializer; the bag is `Unknown` | `src_mcp/core/Rounds/SessionState.cs:80-82`, `:159`, `:143`, `:145`, `:156-157` |
| F2 | `Uniform(maxRounds, threshold, onExhausted = Human)` | `SessionState.cs:277-278` |
| F3 | The policy is READ in one place, the machine's `Exhausted`. Nothing writes it after construction (no `with { OnExhausted = … }` in `src_mcp`, `src_bench`, `src_server`, tests included) | `src_mcp/core/Rounds/RoundMachine.cs:633`, `:656` |
| F4 | `HumanGate = true` is written at three sites only, all inside a round's completion | `RoundMachine.cs:447`, `:513`, `:651` |
| F5 | Every begin refuses a held session. The plan begin checks `AwaitingResolve` first; the others check `HumanGate` first | `RoundMachine.cs:122-134`, `:222-231`, `:282-289`, `:308-316` |
| F6 | `Resolve` keeps a hold and sets `AwaitingResolve = false`; it advances the stage when `AdvanceOnResolve` is set, held or not. `Revise` and `Escalated` do not reset `AdvanceOnResolve` | `RoundMachine.cs:754-777` (`:758`, `:764`), `:469`, `:645-647` |
| F7 | The begin: the claim, the load, `ApplyAnyHumanDecision`, the `SecurityLane` refresh, `loaded` | `src_mcp/src/Server/Rounds/RoundEngine.cs:252`, `:261`, `:267`, `:277-280`, `:285` |
| F8 | `ApplyAnyHumanDecision` re-issues a hold with no recorded question; a person's Continue or Fix is saved there | `RoundEngine.cs:93-120` (`:100-103`, `:118`); `:154-168` |
| F9 | A raised hold is saved once, with its notice id, then the notice is written | `RoundEngine.cs:633-646`, `:702` |
| F10 | `status` and `ask_human` load with no claim; `resolve` loads under its claim; `open` loads and saves with no claim and reads no hold | `src_mcp/src/Server/PanelService.cs:567`, `:1647`, `:1656`, `:523-537`; `src_mcp/src/Server/AskHumanService.cs:100-101` |
| F11 | `HeldSentence` has one caller, which loads the document's current session with no claim | `PanelService.cs:1152-1158`; `RoundEngine.cs:135-140` |
| F12 | The claim is an OS file lock, one per session, tried once, not re-entrant. `ask_human` already takes it briefly to record a question, re-reading under it | `src_mcp/src/Server/SessionClaim.cs:44-58`; `AskHumanService.cs:355-401` |
| F13 | `Load` answers `null` for a file that does not parse; `Normalised` builds `new PanelConfig { Catalog = live }` for a file with no config | `src_mcp/src/Server/SessionStore.cs:514-531`, `:687-707` (`:704`) |
| F14 | A session is serialised in one place, after the scrub. The scrub drops from a bag every name of the type's own contract | `SessionStore.cs:578`; `src_mcp/src/Server/SessionScrub.cs:72-82`, `:132-135` |
| F15 | The session context: names without case, camelCase, enums by name, null not written. `SessionStore.FileWriter` is a seam for a failing write | `src_mcp/src/Server/ServerJsonContext.cs:364-369`; `SessionStore.cs:490` |
| F16 | `Expire` answers `false` both for a question that is not there and for a write that failed. `ReadQuestion` answers `null` for absent, torn and busy alike. One rewrite at a time (`RewriteGate`) | `src_mcp/src/Server/Escalations.cs:244-245`, `:288-307`, `:325-343`, `:286` |
| F17 | The extension shows a card only while its `status` is not `expired`; an expired card is not drawn at all. `translationNote` is drawn on an active card as "shown untranslated: …" | `src_vs_code/src/escalations.ts:117-119`; `src_vs_code/src/activeQuestions.ts:80` |
| F18 | A question no session holds is deleted seven days after it ended (the expiry stamp, or the asking) | `src_mcp/src/Server/EscalationRetention.cs:27` and its summary |
| F19 | An empty `env` value does not override the settings file. A malformed, empty or denied settings file reads as no settings | `src_mcp/src/Server/SettingsFile.cs:87-88`, `:108-126` |
| F20 | `PolicyOf` lower-cases and does not trim; `WhyExhausted` complains for a non-empty word that fell to `Human` and is not `human` | `src_mcp/src/Server/PanelSettings.cs:1082-1088`, `:1215-1224`; `Config` `:1679-1683`; `Rounds = new()` `:253` |
| F21 | The committed member list of `PanelConfig` is `onExhausted, catalog, securityLane, roles` — an ignored member is listed too | `src_mcp/tests/ASessionFileKeepsWhatANewerServerWroteTests.cs:405-419` (`:407-408`) |
| F22 | The bench reads `state.config` from the session FILE | `src_bench/CoaiBench/Running/SettingsApplied.cs:36-75` |
| F23 | Production code builds a `PanelConfig` at five sites: the settings default, `Config`, the null-config path, two backfill stubs | `PanelSettings.cs:253`, `:1680`; `SessionStore.cs:704`; `src_mcp/src/Server/Backfill/StoredHistory.cs:38`, `BackfillWriter.cs:74` |

Read at `47550a7c` for epoch 2 (F1–F23 hold there unchanged).

| # | Fact | Where |
|---|---|---|
| F24 | One read of the settings file answers `Values`, `Missing` or `Busy`. A malformed, empty or denied file is `Values` with nothing in it, and so is a file whose root is not an object. `Busy` is a sharing or lock violation only (two HResults) | `src_mcp/src/Server/SettingsFile.cs:110-128`, `:136-142`; `src_mcp/src/Server/SettingsRead.cs:15-29` |
| F25 | The host records the file's stamp BEFORE its first read, and that read goes through `Layer`, which answers a busy and an unusable file as no values. `Current` reads again only when the stamp differs. A reload keeps the last good service for `Busy` and publishes everything else under the new stamp | `src_mcp/src/Server/PanelServiceHost.cs:88-89`, `:119-124`, `:99-110`, `:143-160` |
| F26 | Only the host builds a `PanelService`. `Program` reads the file once more for its own startup notes, and the host's first build says nothing | `PanelServiceHost.cs:190`; `src_mcp/src/Program.cs:2151-2158`, `:2188-2189`; `PanelServiceHost.cs:214-220` |
| F27 | `resolve` with no decisions is refused while findings are pending, unless a person's Proceed is on file. `Finish` is the one path that advances a stage: it closes the cadence with `Rounds[^1].Verdict` when a code stage moves to done, saves, and records the decisions against `Rounds[^1]` | `PanelService.cs:1675-1682`; `:1775-1810` (`:1792`, `:1806`) |
| F28 | `RecordRoundRow` is private. Its one caller, `RecordRound`, rewrites every column of the row from a `RoundContext` (plan text, head sha, base ref, caller, agent log, commands, plan shape), deletes and re-inserts the reviewers and re-records the findings. A narrow writer of one column exists (`RecordConsultMissed`), and a writer in a file of its own gets a command through `Command()`. `Projection.Write` swallows every failure | `src_mcp/src/Store/RoundsDb.cs:849-933`, `:196-210`, `:340-349`, `:181`; `BackfillWriter.cs:54-56` (*Never `RecordRound`*); `src_mcp/src/Store/Projection.cs:24-38` |
| F29 | `CurrentAnswer.For` is the newest answer among the hold's own questions, or null. `ReadAnswer` answers null for a file that is absent, malformed or being written | `src_mcp/src/Server/Rounds/CurrentAnswer.cs:44-45`; `Escalations.cs:346-368` |
| F30 | Every review entry has one check before `stage.Begin` that answers a sentence of its own — a person's Proceed on file. A fresh document review beside the current one answers `HeldSentence` for a held session and `StillOpen` ("a round nobody has decided on yet") for one that awaits its resolve | `RoundEngine.cs:267-274`, `:294`; `RoundMachine.cs:112-113`, `:137-138`; `PanelService.cs:1152-1158`; `src_mcp/src/Server/DocumentSessions.cs:86-89` |
| F31 | `Rounds` is the session's list of round records, and `Note` a member of each. While a hold stands nothing is appended: a begin refuses first (F5), and a skipped round is recorded only after a begin that passed | `SessionStore.cs:301`, `:135-136`, `:175`; `RoundEngine.cs:294-304`, `:369`, `:423`, `:751` |
| F32 | The cadence records an epic as closed only when the cadence is on and the session holds a plan identity | `src_mcp/src/Server/Cadence/CadenceDesk.cs:293-315` |

**Measured 2026-11-16** — SDK 10.0.114, `net10.0`, the source generator with the session context's options (F15), the type in
one assembly and the context in another, warnings as errors. Scratch projects, not in the repository. The real
`PanelConfig` was NOT measured: T9, T11 and T14 are that measurement.

| # | Shape | A file with no `autonomous` reads as |
|---|---|---|
| M1 | a constructor parameter with a declared default (`bool? Autonomous = true`) | **`true`** — the declared default. Trap 1 |
| M2 | an `init` property with an initializer (`= true`) | `null` through the generated context, **`true`** through the reflection serializer. Trap 2: the meaning would depend on which serializer read the file |
| M3 | the shape of § S1.2 (a file constructor whose `autonomous` parameter has no default) | `null` through the generated context. Through the reflection serializer also `null` — measured on the same shape with a public `init` |
| M4 | § S1.2, other inputs | `"autonomous": null` → `null`; `"AUTONOMOUS": false` → `false`; `false` then `true` → `true` (the last wins); `false` then `"Autonomous": null` → `null`; `"false"` or `0` → `JsonException`; `"isAutonomous": true` alone → `null`, not in the bag, not written back; no `onExhausted` → `Human`; an unknown member → the bag, written back |
| M5 | a constructor parameter `StagePolicy?` against a property `StagePolicy` | `InvalidOperationException` at first use ("each parameter … must bind") — the file constructor's policy parameter is not nullable |
| M6 | § S1.2 in code | `new()`, `new(roles)`, `new { Catalog = x }`, `Uniform(3, 2)` → `GoodEnough`, `true`. `new(null, Human)`, `new(OnExhausted: Human)`, `new(Roles: null, GoodEnough)`, `Uniform(3, 2, p)` → that policy, `false`. `new(null, autonomous: null)` → `Human`, `null`, and no `autonomous` when written |

## S1.1 — Decisions

D1–D7 answer the seven inputs of the stories document's § 7. D8 and D9 are F0 and F1 of the plan round. D10–D15 are
the parent's epoch 2 (§ 4.3 and § 6 as revised).

| # | Input | Decision |
|---|---|---|
| D1 | The marker is absence | `Autonomous` is `null` only when a session file had no `autonomous` member. A file is read through a constructor of its own whose `autonomous` parameter has NO default (M3). Code builds a config through another constructor, which always writes `true` or `false`. The null-config path (`SessionStore.cs:704`) calls the file constructor with `autonomous: null` and `Human`: a file with no config is a file from before `auto`. No initializer, no positional default (M1, M2) |
| D2 | A whole file through an older build | The enum stays `GoodEnough`; no new name is written. The tree at `1f0ae332` IS a build before `auto` that has the bag, so T8 is written and seen GREEN there first (commit 1), with its break-it. After the member lands, the same test, unchanged, pins this build's own load and save. A released older binary is not run (§ S1.7 J8) |
| D3 | Which loads take the claim | The begin and `resolve` release under the claim they hold. `status`, `ask_human` and the fresh-document check take the session's claim for the release only, once, and read the session again under it. Busy → nothing is written, the call goes on with the session as read and the reason "another call holds this session" (D13); the release is done by the call that holds the claim, or by the next call |
| D4 | The hold with no recorded question | `Released` runs before the re-issue branch (`RoundEngine.cs:100`), so under `auto` such a hold is released and nothing is re-issued. A review entry whose release could not be written returns before that branch too (D13): under `auto` no notice is re-issued for a hold from before `auto`. Under any other policy it is re-issued as today, and its config stays without the marker (D8). A hold this build raised is untouched |
| D5 | The defaults and the existing tests | Two steps. Before the flip, one commit names `human` in every test file that speaks of a hold (a list made by one `grep`), with defaults unchanged. Then the flip; a test it reddens gets `human` named and is listed. The other tests run under the shipped default (§ S1.9) |
| D6 | The withdrawal's reason | In the Warning line only. An expired card is not drawn (F17), so no member would show it; the card leaves the active list |
| D7 | `CurrentPolicy`'s home | The decisions are pure and live in core: `LegacyHold` and `PanelConfig.UnderPolicyOf`. The reads and writes are `SessionPolicy` (`src_mcp/src/Server/Rounds/SessionPolicy.cs`), built once in `PanelService` and handed to the engine and to `AskHumanService` |
| D8 | F0 — provenance lost by a stamp under `human` | `LegacyHold.Stamped` leaves the config of a session exactly as loaded while a hold from before `auto` stands. The marker is written only when no such hold stands. The begin stamps AFTER the person's decision is applied (`:279`, after `:267`), so a hold raised after a person released the old one is this build's (T45) |
| D9 | F1 — the window between the release and the withdrawal | `Escalations.Withdraw` answers `Withdrawn`, `NotThere` or `Failed`. Every notice is withdrawn first; any `Failed` → nothing is saved, the hold and its `HoldQuestions` stay, a Warning, and the next deciding load tries again. Then the released session is saved; a save that fails leaves the hold too. `Withdraw` does not rewrite an expired card, so a second pass changes nothing |
| D10 | U1, settled by the parent (entry 1.2, § 4.3) | The released state is `HumanGate = false`, `HoldQuestions = []`, `AwaitingResolve = true`, `AdvanceOnResolve = true` — whether the held round had been resolved or not. Nothing in S1 advances a stage: the next `resolve` does, through `PanelService.Finish` (F27), so the cadence closes and the decisions are recorded on the one path that does both. `resolve` with no decisions is accepted only when nothing is pending (F27): a caller whose held round still has open findings passes its decisions in that one call. The parent's "with the decisions still open, or none" says the same |
| D11 | The person's answer wins (parent § 4.3) | A release is written only while NO answer is on file for any question of the hold — `CurrentAnswer.For(session, escalations) is null`, the reader every other consumer uses (F29). Words with no button count: the person spoke. It is asked twice: before anything is withdrawn, and again after the withdrawal and before the save. An answer found either time is left to today's readers (`ApplyAnyHumanDecision`, `PersonsDecision`); `Released` writes nothing, and the config keeps no marker (D8) |
| D16 | The cards of an answer that raced (parent § 4.3, *"As today" includes the card*; U3 settled) | When the second read finds an answer, every card this call withdrew is put back by `Escalations.Restore` (§ S1.4): `Status` open, `ExpiredUtc` empty, nothing else — the state it had, because `Withdraw` writes only open cards. Only those cards: one that had run out its own wait stays expired. A restore that fails is a Warning and never a reason: the answer stands. **The next deciding load tries again, in step 2 of `Released`**: while the person's answer is on file for a hold from before `auto`, any NOTICE of that hold that is expired is restored — a standing hold's notice is expired by nothing but `Withdraw` (`Expire` has one caller, `AskHumanService.cs:328`, for the question it waited on). It is done there and not in `OfferHeldNotices`, which also runs after a release that could not be written (it would put back a card the next call withdraws again) and under `human` (where it would change the parent's residual). `OfferHeldNotices` then lists the decisions on the restored notice, as today. Nothing is re-issued: `Withdraw` deletes no card, so a restore can only fail on a card that is still there; an `ask_human` question card whose restore failed stays expired, as one that ran out its wait does, and the notice carries the hold's decisions. Such a hold is the person's and answers `GateHeld` as today (requirement 4: "unless the person answered it first") |
| D12 | The note on the held round (parent § 4.3) | `LegacyHold.Note` — *"released by policy auto: this hold was raised by a build before auto"* — is written on `Rounds[^1]`, the held round (F31), in the SAME save as the release. Its verdict is not touched. A session with no round is released without a note. Then the note is projected into `coai.db`'s `rounds.note` for that round by a writer of that one column (`RoundNotes.Write`, § S1.5.1) through `Projection.Write`: after the save, best effort, never able to undo the release. **Not through `RecordRound`** (F28) — reported as U2, and now the parent's own words: a one-column update |
| D13 | What a review entry answers (parent § 4.3) | Two sentences in `LegacyHold` (§ S1.3). After a release, until the `resolve`: `ReleasedSentence(stage)`, never `Unresolved` and never `StillOpen`. A release that could not be written: `NotWrittenSentence(stage, why)`, never `GateHeld`. `why` is one of four clauses: the settings file could not be read; another call holds this session; a notice could not be withdrawn; the session could not be saved. Both are answered in the engine's one hold check, beside the Proceed sentence (F30), and by the fresh-document check. "Released and not yet resolved" is read from the session itself: not held, `AwaitingResolve` and `AdvanceOnResolve` set, and `Rounds[^1].Note == LegacyHold.Note` — no new member on disk |
| D14 | A read that failed (parent § 6) | `SettingsRead` gains `Unusable(Why)`: a file that is there and gave no values. The host does not record the stamp of a read that was busy or unusable, so the next call reads the file again; it builds one service per such stamp, not one per call. The settings such a read gave carry `PolicyUnread = true` unless the process environment names the policy. `Released` writes nothing while it is set: the reason is "the settings file could not be read". A file that is genuinely missing is a read that succeeded (§ S1.5.2) |
| D15 | Who owns the note for a failed read | **S1, text included.** The parent asks for a startup note AND a Warning (§ 6), its test is in step 4 (S1's), and this repository writes a log line and its notice in ONE call (`PanelServiceHost.cs:207-210`, `StartupNotices`). So the fact, the Warning and the notice are one method, called by the host for the read its service is built on. S3 owns the other startup note — a policy pinned in the process environment (stories document § 9.3) — and the documents; it adds nothing to this one |

## S1.2 — The value: `PanelConfig`, the words, the defaults

`PanelConfig` stops being positional. Everything not shown stays as it is (`PlanDefault` … `Uniform`'s neighbours).

```csharp
public sealed record PanelConfig
{
    /// <summary>A config made IN CODE. No policy named is policy auto; a policy named is that policy and never auto.</summary>
    public PanelConfig(IReadOnlyDictionary<string, RoleGate>? Roles = null, StagePolicy? OnExhausted = null)
    {
        this.Roles = Roles ?? Defaults();
        this.OnExhausted = OnExhausted ?? StagePolicy.GoodEnough;
        Autonomous = OnExhausted is null;
    }

    /// <summary>A config as a SESSION FILE holds it: the deserialiser's constructor, and the store's for a file with no config.</summary>
    [JsonConstructor]
    public PanelConfig(IReadOnlyDictionary<string, RoleGate>? roles, bool? autonomous, StagePolicy onExhausted = StagePolicy.Human)
    {
        Roles = roles ?? Defaults();
        OnExhausted = onExhausted;
        Autonomous = autonomous;
    }

    public IReadOnlyDictionary<string, RoleGate> Roles { get; init; }
    public StagePolicy OnExhausted { get; private init; }
    public bool? Autonomous { get; private init; }

    [JsonIgnore] public bool IsAutonomous => Autonomous == true;
    [JsonIgnore] public bool PredatesAuto => Autonomous is null;

    /// <summary>This config under the policy of <paramref name="current"/> — both halves, never one.</summary>
    public PanelConfig UnderPolicyOf(PanelConfig current) =>
        this with { OnExhausted = current.OnExhausted, Autonomous = current.IsAutonomous };
}
```

Rules the coder keeps, each pinned by a test:

- **`autonomous` in the file constructor never gets a default**, and `Autonomous` never gets an initializer. The remarks on
  the member cite M1–M3. The only declared default there is today's: no `onExhausted` reads `Human`, as it always did.
- **The file constructor is called from code at one production site**, `SessionStore.cs:704`:
  `new PanelConfig(roles: null, autonomous: null) { Catalog = live }`. Tests may call it to build a config "as a build
  before `auto` wrote it".
- **`Uniform` tells an explicit policy from its default by a nullable parameter**:
  `Uniform(int maxRounds, int threshold, StagePolicy? onExhausted = null) => new(<the dictionary>, onExhausted)`.
  The constructor decides; `Uniform` passes the value through.
- **`PolicyOf` returns `StagePolicy?`** — `null` means no policy is named:
  `null or "" or "auto" => null`, `"continue"`, `"human"`, `"escalate"`, `"good_enough" or "goodenough"` → themselves,
  `_ => StagePolicy.Human`. No trim (F20). `Config` (`PanelSettings.cs:1680`) and `WhyExhausted` keep their text: the first
  hands the value to the code constructor, the second compares with `StagePolicy.Human` as today. `auto` is spelled in
  one place on the server, and what "no policy named" means is decided in one place, the code constructor.
- **The invariant** `IsAutonomous` ⇒ `OnExhausted == GoodEnough` holds for every config code can make: the code
  constructor and `UnderPolicyOf` write the pair together, and nothing outside the type can write either half.
- The parameter names `Roles` and `OnExhausted` stay capitalised: call sites name them (`PanelSettings.cs:1680`, six
  tests). If `dotnet format` refuses the casing, rename both to lower case and fix those call sites in the same commit.
- `PanelSettings.Rounds = new()` (`:253`) and the two backfill stubs (F23) need no edit. The stubs are never saved.
- The committed list (F21) gains `autonomous`, `isAutonomous`, `predatesAuto`. Nothing is retired.
- **C5.** `CodeRoundHarness.Service(...)` (`src_mcp/tests/CodeRoundHarness.cs:107-133`) gains a last parameter
  `bool autonomous = false`; `Rounds = autonomous ? PanelConfig.Uniform(maxRounds, threshold) : PanelConfig.Uniform(maxRounds, threshold, onExhausted)`.

## S1.3 — The pure half: `src_mcp/core/Rounds/LegacyHold.cs` (new)

```csharp
/// <summary>A hold a build before policy auto raised — the only hold a configuration may release.</summary>
public static class LegacyHold
{
    public static bool Stands(SessionState s) => s.HumanGate && s.Config.PredatesAuto;

    /// <summary>The session as an auto verdict would have left it: nothing held, the stage advances on the next resolve.</summary>
    public static SessionState Released(SessionState s, PanelConfig current) => s with
    {
        HumanGate = false,
        HoldQuestions = [],
        AwaitingResolve = true,          // always — the parent's § 4.3, entry 1.2 (D10)
        AdvanceOnResolve = true,
        Config = s.Config.UnderPolicyOf(current),
    };

    /// <summary>The config a deciding load reads: the settings' policy — but untouched while a hold from before auto stands.</summary>
    public static PanelConfig Stamped(SessionState s, PanelConfig current) =>
        Stands(s) ? s.Config : s.Config.UnderPolicyOf(current);

    /// <summary>What the held round's record says once policy auto released its hold. The verdict stays what it was.</summary>
    public const string Note = "released by policy auto: this hold was raised by a build before auto";

    /// <summary>What a review entry answers after a release, until the resolve that moves the stage.</summary>
    public static string ReleasedSentence(string stage) =>
        $"policy auto released this {stage} hold, raised by a build before auto; call resolve (with the decisions still open, or none) and the stage moves on";

    /// <summary>What a review entry answers while the release could not be written. Never GateHeld: no person has to decide.</summary>
    public static string NotWrittenSentence(string stage, string why) =>
        $"policy auto is releasing this {stage} hold, raised by a build before auto; the release could not be written yet ({why}) — call again";

    // The four reasons a release is not written, and no fifth.
    public const string SettingsUnread = "the settings file could not be read";
    public const string ClaimBusy = "another call holds this session";
    public const string NoticeNotWithdrawn = "a notice could not be withdrawn";
    public const string SessionNotSaved = "the session could not be saved";
}
```

`Stamped` returns a config, so it cannot touch `EscalationsUsed`, `RoundsRunThisStage` or the pending decisions (parent
§ 4.1). `Released` stamps in the same step, so a released session is saved once and never keeps an absent marker.
`Released` does not move `Stage`, `Rejections`, `RoundsRunThisStage` or `SecondRound`: the stage moves in
`RoundMachine.Resolve`, reached through `PanelService.Finish` (D10). The two sentences are the parent's, word for word
(§ 4.3); `{stage}` is `RoundSubject.StageName(session.State.Stage.ToString())`, as `HoldNotice` names a stage
(`RoundEngine.cs:859`). They are constants of this type so that a test compares with the constant and S3 can quote
the same words.

## S1.4 — `Escalations.Withdraw` (`src_mcp/src/Server/Escalations.cs`)

```csharp
/// <summary>What <see cref="Escalations.Withdraw"/> did with one question of a hold that no longer stands.</summary>
public enum Withdrawal { Withdrawn, NotThere, Failed }

public Withdrawal Withdraw(string id, DateTime nowUtc)
internal Action<string, string> Writer { get; init; } = AtomicJson.Write;   // the seam SessionStore.FileWriter is (F15)
```

Under `RewriteGate`, in this order:

| The question file | Answer | What is written |
|---|---|---|
| is not there | `NotThere` | nothing |
| does not parse (`JsonException`) | `NotThere` — no reader can show it (`parseEscalation` skips it) | nothing |
| reads, `Status == expired` | `NotThere` | nothing — `ExpiredUtc` keeps its first value |
| reads, open, the write succeeds | `Withdrawn` | `Status = expired`, `ExpiredUtc = nowUtc` |
| cannot be read or written (`IOException`, `UnauthorizedAccessException`) | `Failed` | nothing |

It reads under the file's turn and RELEASES the turn before it writes: `AtomicJson.Write` takes the turn itself and the
turn is not re-entrant (`Escalations.cs:395-412`). `WriteAtomic` (`:383`) becomes an instance method over `Writer`.
`Expire` and its callers are not changed.

**`Restore` — a withdrawal taken back** (parent § 4.3, *"As today" includes the card*; D16):

```csharp
/// <summary>What <see cref="Escalations.Restore"/> did with one card policy auto withdrew and must offer again.</summary>
public enum Restoration { Restored, NotExpired, Failed }

public Restoration Restore(string id)
```

Under `RewriteGate`, through the same `Writer`, with `Withdraw`'s rule for the turn:

| The question file | Answer | What is written |
|---|---|---|
| reads, `Status == expired`, the write succeeds | `Restored` | `Status = EscalationStatuses.Open` (the empty string), `ExpiredUtc` empty; no other member changes |
| reads, open | `NotExpired` — the second call, or a card nobody withdrew | nothing |
| is not there, or does not parse | `NotExpired` | nothing |
| cannot be read or written (`IOException`, `UnauthorizedAccessException`) | `Failed` | nothing |

`Withdraw` writes only an OPEN card, and an open card has an empty `Status` and an empty `ExpiredUtc`
(`Escalations.cs:32-34`). So `Restore` on a card `Withdraw` answered `Withdrawn` for puts back exactly the two members
that call changed: every member reads back as it read before the withdrawal, the answer file is not touched, and
the extension draws the card as it drew it before (F17). (Not byte for byte: a card an older build wrote is
re-serialised by `Withdraw` already.) It takes no time and no reason, so it is idempotent by construction. It does not know who expired a card; its two callers decide that (§ S1.5, steps 2 and 5).

## S1.5 — `SessionPolicy` and the five entries (`src_mcp/src/Server/Rounds/SessionPolicy.cs`, new)

```csharp
/// <summary>What a deciding load found: the session to go on with, and — for a hold from before auto that policy auto
/// could not release this time — why (one of LegacyHold's four reasons). Empty in every other case.</summary>
internal readonly record struct Release(PersistedSession Session, string NotWritten);

internal sealed class SessionPolicy(
    PanelSettings settings, SessionStore store, Escalations escalations, Store.Projection projection, Serilog.ILogger log)
{
    /// <summary>The config under the current settings — <see cref="LegacyHold.Stamped"/>, on a session.</summary>
    internal PersistedSession Stamped(PersistedSession session);

    /// <summary>Releases a hold from before auto. The CALLER HOLDS the session's claim.</summary>
    internal Release Released(PersistedSession session);

    /// <summary><see cref="Released"/>, then <see cref="Stamped"/> on its session — for a caller that holds the claim.</summary>
    internal Release Current(PersistedSession session);

    /// <summary>The same for a caller that holds NO claim: it takes the claim for the release alone. Null for no session.</summary>
    internal Release? CurrentUnclaimed(PersistedSession? session, string repoPath, SessionAddress at);

    /// <summary>Whether policy auto released this session's hold and the resolve that moves its stage has not come yet (D13).</summary>
    internal static bool AwaitsItsResolve(PersistedSession session) =>
        session.State is { HumanGate: false, AwaitingResolve: true, AdvanceOnResolve: true }
        && session.Rounds is [.., { Note: LegacyHold.Note }];
}
```

`Released`, step by step. Steps 1–3 withdraw nothing and save nothing (step 2 may put a card back); they are one
private method, which `CurrentUnclaimed` asks before it takes a claim.

1. **Not ours.** `!settings.Rounds.IsAutonomous || !LegacyHold.Stands(session.State)` → `new(session, "")` (the same
   object). This is the whole authority check, and it reads the config AS LOADED.
2. **The person's answer wins (D11).** `CurrentAnswer.For(session, escalations) is not null` → `new(session, "")`.
   First, the retry of a restore that failed (D16): `Restore(id)` for every id of `HoldQuestions` whose card reads as
   a NOTICE (`Read(id) is { Kind: EscalationKinds.Notice }`). Almost always every answer is `NotExpired` and nothing
   is written or logged; `Restored` →
   `log.Information("a notice of the {Stage} hold of session {SessionId}, withdrawn by policy auto before the person answered, is offered again", …)`;
   `Failed` → step 5's Warning. Today's readers log what the person chose.
3. **A failed read authorises nothing (D14).** `settings.PolicyUnread` →
   `log.Warning("policy auto did not release the {Stage} hold of session {SessionId}: the settings file could not be read — the hold stays, and the next call reads the file again", …)`;
   `new(session, LegacyHold.SettingsUnread)`.
4. **The notices.** `Withdraw(id, now)` for EVERY id of `HoldQuestions` (no short circuit). Any `Failed` →
   `log.Warning("policy auto could not withdraw notice(s) {Ids} of the {Stage} hold of session {SessionId} — the hold stays, and the next call tries again", …)`;
   `new(session, LegacyHold.NoticeNotWithdrawn)`.
5. **The answers, read AGAIN (D11).** `CurrentAnswer.For(session, escalations) is not null` →
   `log.Information("a person answered the {Stage} hold of session {SessionId} while policy auto was releasing it — their answer stands, nothing is released, and {Restored} notice(s) are offered again", …)`;
   `new(session, "")`. Before that line, **the cards are put back (D16)**: `Restore(id)` for every id step 4 answered
   `Withdrawn` for — those and no other, so a card that had run out its own wait stays expired. Any `Failed` →
   `log.Warning("policy auto could not offer notice(s) {Ids} of the {Stage} hold of session {SessionId} again after the person's answer — the next call tries again", …)`.
   The result is `new(session, "")` either way: the hold is the person's now, and the entry goes on to today's
   readers.
6. **The save.** `released = session with { State = LegacyHold.Released(session.State, settings.Rounds), Rounds = <the list with Rounds[^1] with { Note = LegacyHold.Note }, or the list as it is when it is empty> }`;
   `store.Save(released)`. `SessionStoreException` →
   `log.Warning(e, "policy auto could not save the release of the {Stage} hold of session {SessionId} — the hold stays, and the next call tries again", …)`;
   `new(session, LegacyHold.SessionNotSaved)`.
7. **The projection (D12).** When there is a round:
   `projection.Write(db => RoundNotes.Write(db, state.SessionId, round.Stage, round.Number, round.LoggedNote), "the release note")`.
   `Projection.Write` swallows and logs a failure (F28); the release stands either way.
8. `log.Warning("policy auto released the {Stage} hold of session {SessionId} raised under {Policy} — a hold from a build before auto; {Withdrawn} notice(s) withdrawn", stage, id, <OnExhausted as loaded>, <count of Withdrawn>)`;
   `new(released, "")`.

No line carries a question's text or an answer's. `Current(session)` is `Released(session)` with `Stamped` applied to
its session; `NotWritten` is passed through.

`CurrentUnclaimed`: `null` → `null`. Steps 1–3 decide without a claim: not ours → `new(Stamped(session), "")`;
answered → `new(session, "")`; unread → step 3's Warning and reason. Else
`SessionClaim.TryTake(settings.DataDir, repoPath, at.Branch, at.Document, at.Feature)`; `null` → an Information line and
`new(session, LegacyHold.ClaimBusy)`. Else load the session AGAIN under the claim and return `Current(fresh)` (or
`new(session, LegacyHold.ClaimBusy)` when it does not read just now: a writer that takes no claim — `open`, the startup
sweep — is replacing it).

| Entry | Where | The call | Claim |
|---|---|---|---|
| begin — `review_plan`, `review_code`, `review_document`, `review_feature` | `RoundEngine.cs:267-274` | the block below | the round's own (`:252`) |
| begin, the stamp | `RoundEngine.cs:279` | `{ Config = LegacyHold.Stamped(session.State, _settings.Rounds) with { SecurityLane = _settings.SecurityLane.Gate } }` | the same |
| `resolve` | `PanelService.cs:1656-1662`, after the null check, before `OfferHeldNotices` | `session = _policy.Current(session).Session;` | its own (`:1647`) |
| `status` | `PanelService.cs:567` | `var session = _policy.CurrentUnclaimed(_store.Load(…), repoPath, at)?.Session;` | taken for the release |
| `ask_human` | `AskHumanService.cs:100`, before `_desk.Facts` | `var release = _policy.CurrentUnclaimed(_store.Load(…), repoPath, at); var session = release?.Session;` — the whole `Release` kept in a local, so S3 reads `release?.NotWritten` without touching this line (X8) | taken for the release |
| a fresh document review (`HeldSentence`'s caller) | `PanelService.cs:1152-1158` | `CurrentUnclaimed` on the loaded document session, `at = new SessionAddress(branch, identity, string.Empty)`, then the `switch` below | taken for the release |

The begin, in `RoundEngine.RunStageAsync` — one hold check for every review entry, as today:

```csharp
var release = _policy.Released(session);
if (release.NotWritten.Length > 0)
{
    // Before ApplyAnyHumanDecision: under policy auto nothing is re-issued for a hold from before auto (D4).
    return Error(LegacyHold.NotWrittenSentence(StageName(session), release.NotWritten), from);
}

session = ApplyAnyHumanDecision(release.Session);
if (ProceedOnFile(session) is { Length: > 0 } pressed)      // as today
{
    return Error(pressed, from);
}

if (SessionPolicy.AwaitsItsResolve(session))
{
    return Error(LegacyHold.ReleasedSentence(StageName(session)), from);
}
```

Nothing is saved for either refusal. The fresh-document check, `PanelService.WhyNotAFreshReview`:

```csharp
_policy.CurrentUnclaimed(<today's load>, repoPath, at) switch
{
    null => null,
    { NotWritten.Length: > 0 } release => LegacyHold.NotWrittenSentence(StageName(release.Session), release.NotWritten),
    { Session.State.HumanGate: true } release => _engine.HeldSentence(release.Session),
    { Session: var released } when SessionPolicy.AwaitsItsResolve(released) => LegacyHold.ReleasedSentence(StageName(released)),
    { Session.State.AwaitingResolve: true } => DocumentSessions.StillOpen(document),
    _ => null,
};
```

`SessionPolicy` is built in `PanelService` after `_projection` (`:105`) and handed to `AskHumanService` (`:116`) and
`RoundEngine` (`:144`). `RoundEngine.cs` grows by at most twelve lines (the parameter, its field, the block above);
every sentence and every decision lives outside it. `HeldSentence`, `WithTheHoldReissued` and `ApplyAnyHumanDecision`
keep their bodies. `open` is not an entry: it reads no hold and keeps the config it loaded.

**What each call answers, by what `Released` found.** Rows 1 and 2 are new sentences; rows 3 and 4 are today's.

| The session | A review entry, and the fresh-document check | `resolve` | `status` | `ask_human` |
|---|---|---|---|---|
| 1. released — by this call or an earlier one — and not resolved since (`AwaitsItsResolve`) | `ReleasedSentence(stage)`; never `Unresolved`, `StillOpen` or `GateHeld` | advances the stage, once (D10); with nothing pending `[]` is enough, with findings pending the caller passes its decisions (F27) | `awaitingResolve: true` | not held, so not the card door: S3's conversation road |
| 2. a hold from before `auto`, the release not written (`NotWritten`) | `NotWrittenSentence(stage, why)`; never `GateHeld`; nothing re-issued | today's: decisions recorded, the hold stays; a caller's claim is refused with `ClaimOnAHeldGate` | held, as on disk | S3 (N3, third row — the `Release`'s `NotWritten` is not empty): at once, "call again" |
| 3. a hold from before `auto` that the person answered | today's, because the answer is applied as today: Continue or Fix → a fresh set of rounds; Proceed → `ReviewAfterProceed`; Discuss or words → `GateHeld` — the person spoke, and the hold is theirs | today's | today's | S3 (N3, second row — `NotWritten` empty, `Stands` true: the person's) |
| 4. a hold this build raised | `GateHeld` / `HeldSentence` | today's | today's | S3 (N3, first row — `NotWritten` empty, `Stands` false: the person's) |

### S1.5.1 — The note in `coai.db`: `src_mcp/src/Store/RoundNotes.cs` (new)

```csharp
/// <summary>One round's note, and nothing else of its row — for a writer that holds no RoundContext.</summary>
internal static class RoundNotes
{
    /// <returns>The rows changed: 1, or 0 when the database holds no such round — nothing is inserted.</returns>
    internal static int Write(RoundsDb db, string sessionId, string stage, int number, string note);
}
```

`UPDATE rounds SET note = $note WHERE session_id = $session AND stage = $stage AND number = $number`, on a command from
`db.Command()` (F28) — the shape of `RecordConsultMissed`. No schema step: the column is step 16's. `RoundsDb.cs` is not
edited (it is past the file ceiling, which is why `Command()` exists). Written twice, it writes the same note.

**Why not `RecordRound`** (F28, U2): a release holds no `RoundContext`, no reviewers and no findings. Through
`RecordRound` it would write the round's plan text, head sha, base ref, caller, agent log, commands and plan shape as
empty, and re-record its findings from a list it does not have — the loss `BackfillWriter` names for its own narrow
write. A row the database does not hold is not created: the projection is rebuilt from the session files, and the
note is in the session file.

### S1.5.2 — A settings read that failed (D14, D15)

**The read.** `SettingsRead` gains one case, and `Values` then always means a parsed object:

```csharp
/// <summary>The file is there and gave no values: malformed, empty, not an object, denied, or an I/O failure that is not a sharing violation.</summary>
public sealed record Unusable(string Why) : SettingsRead;
```

| `TryRead` meets | Answer | `Why` |
|---|---|---|
| a JSON object (also `{}`) | `Values` | — |
| no file, no folder | `Missing` | — |
| a sharing or lock violation (F24) | `Busy` | — |
| text that is not JSON, or no text | `Unusable` | `it is not JSON this server can read` |
| JSON whose root is not an object | `Unusable` | `it is not a JSON object` |
| `UnauthorizedAccessException` | `Unusable` | `access to it was denied` |
| any other `IOException` | `Unusable` | `it could not be opened` |

`ValuesOf` answers no values for `Unusable`, so `Read`, `Layer` and every one-shot mode behave as today (F26: none of
them builds a service). `SettingsLayer.Layered` gains a second member, `string Unread = ""`: `TryLayer` answers
`Layered(lookup, why)` for `Unusable` and keeps `SettingsLayer.Busy`. One new method serves the host's first build:

```csharp
/// <summary>The layer a START builds on: always a lookup, and why the file gave nothing — "" when it was read or is missing.</summary>
internal static SettingsLayer.Layered StartLayer(string dataDir, Func<string, string?> environment, Action<string> said, Func<string, string> readText);
```

It is `TryLayer`, with `Busy` answered as `Layered(the environment alone, "it is held open by its writer")`.

**The host** (`PanelServiceHost.cs`). `Never = (DateTime.MaxValue, -1)` is a stamp no file has (the value `_warned`
starts on).

| Where | Today | Becomes |
|---|---|---|
| the constructor (`:88-89`) | `_stamp = Stamp(); _current = Start();` | the stamp is taken first, as today; `Start()` builds on `StartLayer`; `_stamp` is that stamp when `Unread` is empty, else `Never`, and `_unreadAt` is that stamp |
| `Rebuilt`, `Layered` with `Unread` empty | published under the new stamp | unchanged |
| `Rebuilt`, `Busy` | the last good service, the old stamp | unchanged — and with `_stamp == Never` the next call reads again |
| `Rebuilt`, `Layered` with a reason | published under the new stamp | `stamp == _unreadAt` → `_current`, nothing built and nothing said. Else `_current = Assemble(first: false, layer)`, `_unreadAt = stamp`, `_stamp = Never` |
| `Assemble` (`:185-191`) | `FromEnvironment(configuration)` | `FromEnvironment(layer.Lookup) with { PolicyUnread = layer.Unread.Length > 0 && _env(PanelSettings.Key.Exhausted) is not { Length: > 0 } }`; and, when `layer.Unread.Length > 0`, the note below — on the first build too |

So a file that cannot be read is read again on every call, costs one small read per call and one service per stamp,
and the policy is the operator's on the first call that can read it — whether the file changed or only became
readable. `PanelSettings.PolicyUnread` is `public bool PolicyUnread { get; init; }`, false in every settings object
built any other way: the host is the only road to a service (F26).

**The note — one call, the Warning and the notice together** (`StartupNotices.cs`):

```csharp
/// <summary>The settings file is there and gave no values to the read a service was built on.</summary>
internal static void SettingsUnread(string sentence, string settingsPath, Noticing noticing, Serilog.ILogger log)
{
    log.Warning("settings: {Note}", sentence);
    noticing.Offered(() => Note(ServerNoticeCodes.UnrecognisedSetting, StoodDown, Canonical(settingsPath), sentence));
}
```

An existing code (the extension renders the codes it knows; no new one in E1), the class of a setting this build fell
back on, the subject `Adopted` uses. The sentence, built in `PanelServiceHost`:

- no policy in the process environment: *"{path} exists but could not be read ({why}). Every setting in it is at its
  default, so When the rounds run out is Auto by default: the gate holds for nobody, and a hold from a build before
  Auto is not released until this file can be read. Repair or remove the file, or save the settings once from the
  panel — it is read again on the next call."*
- the process environment names the policy: the middle sentence is *"Every setting in it is at its default;
  COAI_ON_EXHAUSTED comes from this server's own environment and stands."*

`Program.cs` is not edited: its own read is for the notes of `StartupNotices.Record`, and the read a release rests on
is the host's. A reload that finds the file busy keeps today's line and writes no note: no default is taken — the last
good settings keep serving, and they came from a read that succeeded, so `PolicyUnread` stays as that read left it.

## S1.6 — The bench (N2): `src_bench/CoaiBench/Running/SettingsApplied.cs:66-75`

`autonomous` is read from `config` as `true`, `false` or absent (a member that is not a boolean is absent). Asked `auto`
(any case): no mismatch only for `GoodEnough` with `autonomous: true`. Any other asked word: today's comparison, and
`autonomous: true` is a mismatch. Not asked: nothing compared. The message keeps today's opening,
`on-exhausted asked '{asked}', session says '{seen}'`, and adds `with autonomous true`, `with autonomous false` or
`with no autonomous member` when the marker decided it. The comparison is one private method.

## S1.7 — The security judgement

**The property.** A hold is released by the person's answer on its notice. This story adds one road and no other: the
server releases a hold when the CURRENT POLICY IS `auto` AND THE HOLD'S STORED CONFIG HAS NO `autonomous` MEMBER — a hold
raised by a build that could not have meant "ask a person, not `auto`", because it did not know `auto`. The owner chose
that release (O2). Epoch 2 narrows the road twice and widens it nowhere: the policy must come from a settings read that
succeeded (J10), and the person must not have answered the hold (J9).

| # | Judgement | Pinned by |
|---|---|---|
| J1 | **The caller's argument releases nothing, under every policy.** `ClaimRefusal` is not changed. `resolve(humanDecision: "proceed")` on a hold this build raised is refused with `ClaimOnAHeldGate` while the settings say `auto` | T41 |
| J2 | **One predicate, read before any stamp.** `LegacyHold.Stands` is the whole test of which hold is eligible, `SessionPolicy.Released` is its only caller that writes, and every entry calls `Released` on the session as loaded. `LegacyHold.Stamped` never writes the marker onto a standing eligible hold, so reading a hold can never change what it is | T18, T22, T32, T44 |
| J3 | **A hold this build raises always carries the marker.** A hold is raised only by a round (F4). A round runs only after a begin that was not refused, a begin refuses every held session (F5), and a session that is not held is stamped before `stage.Begin` (`:279`). So no round ever runs on a config whose marker is absent, and the save that records the hold (F9) writes `autonomous` | T23, T26, T45 |
| J4 | **A later `auto` never releases it.** Its config has `false` or `true`; `Stands` is false; no setting is read after that. However the policy became `auto` — the word written, the key deleted, an empty value, a settings file that cannot be read (F19), a client `env` — the same predicate decides | T39, T40, T43 |
| J5 | **Code cannot make the marker absent.** `Autonomous` has a `private init`; the code constructor writes `true` or `false`; `UnderPolicyOf` writes a `bool`. The file constructor is the one way, and production calls it at `SessionStore.cs:704` only | T5, T6, T15; checklist item 3 |
| J6 | **The release is all or nothing, and repeatable.** Notices first, the session second (D9); the note rides in the same save as the release. A crash or a failed write between them leaves a held session whose config still has no marker; the next deciding load releases it again and rewrites no card. The projection comes after the save and cannot undo it | T30, T31, T51, T53 |
| J7 | **The release is written under the session's claim**, and an entry that takes the claim reads the session again under it — a release never overwrites what a `resolve` saved in between | T33 |
| J9 | **The person outranks the configuration.** A release is written only while no answer is on file for the hold, and the answers are read a second time after the cards are withdrawn — the last moment an answer to a card the panel still offered can exist. An answer found either time is applied by today's readers, by today's rules; the release writes nothing, not even the marker. "As today" includes the card: what the release withdrew before it saw the answer it puts back, so the person can answer again, and such a hold answers `GateHeld` because it is theirs. A restore opens a card only where policy auto closed it — the cards of this call, or a standing hold's notice — and never an `ask_human` card that ran out its wait | T49, T50, T66, T67 |
| J10 | **A read that failed decides nothing about a hold.** The default such a read gives (`auto`) ends rounds, and releases nothing: `Released` stops at `PolicyUnread` before the first withdrawal. The read is repeated on the next call, so an operator's `human` is in force as soon as the file can be read, and a hold from before `auto` is then theirs to keep. A missing file, a readable file and a policy named in the process environment are reads that succeeded | T59–T65 |
| J11 | **The released state is consumed once, by one path.** `AwaitingResolve` and `AdvanceOnResolve` are set together and cleared together by the next `resolve` (F6), so the flag cannot outlive it into a later round — the defect of entry 1.2. S1 never moves `Stage` itself: a stage that moved outside `Finish` would skip the cadence close and the decision record | T19–T21, T56–T58 |

**How absence could be manufactured** (a hold this build raised would read as eligible — the dangerous direction):

| # | Road | What it yields here | Test |
|---|---|---|---|
| A1 | a constructor parameter with a default (M1) | not present: the file constructor's `autonomous` has none | T9; T10 shows the trap |
| A2 | a property initializer (M2) | not present | T9 |
| A3 | the null-config path | `null`, on purpose: a file with no config predates `auto`. It cannot hold a hold this build raised | T14 |
| A4 | the bag, by a member that does not bind | the member binds in any case; the bag stays empty | T11 |
| A5 | the bag, by code putting a known name into it | the scrub drops it; a typed `null` stays absent, a typed value is written once | T13 |
| A6 | a computed name in the file (`isAutonomous`, `predatesAuto`) | ignored, never the marker, never written | T12 |
| A7 | this build saving a config it made | always `true` or `false` | T15 |
| A8 | a save by an older build that has the bag (since #868, `mcp-v0.63.0`) | the member comes back as it was | T8 at commit 1; `AnOlderServersReSave_KeepsEveryMemberItDoesNotKnow` |
| A9 | a save by a build older than that | the member is dropped. **The residual the parent accepts (§ 4.3)**: a hold raised under `human` by this build, such a save, then a switch to `auto` — all three | T8's break-it at commit 1 shows it |
| A10 | `"autonomous": null`, a duplicate member, a member of another type, written into the file | `null`, the last one, or a file that is not read (M4, F13). Each needs a direct write to the data folder — the access that can set `humanGate` to `false` today. Not a configuration road, and not widened | T9 (explicit null), T17 |

**How the marker could be lost the other way** (an eligible hold reads as this build's, and stays — fail closed, but O2
broken): a stamp while the hold stands (D8, T44); the reflection serializer over an initializer (M2, not present).

**J8 — the residuals, and nothing wider.** (a) A9, as the parent words it. (b) The data-folder write class of A10, which
exists today and which [PLAN_a_signed_answer_releases_the_gate.md](../todo/PLAN_a_signed_answer_releases_the_gate.md) names —
the session file, the answer file and `settings.json` alike (parent § 6, the access model). (c) The parent's two
residuals of the order: a card no session records is not withdrawn (F18 retires it), and a withdrawal followed by a
failed save leaves the hold with fewer live cards. (d) An answer file being written at the instant of the second read
reads as no answer (F29); the window is one file write wide. (e) A restore that fails leaves a notice expired until the
next deciding load, and an `ask_human` question card for good (D16). (f) A projection that fails leaves the
note in the session file only; nothing writes it into `coai.db` later. **Not proved by these tests:** that a RELEASED
older `coai-mcp` keeps the member — the evidence is this repository's tree at commit 1 and the bag's own suite; that an
answer was written by a person; and a real sharing violation on Windows — the busy read is driven through the
`readText` seam, as the host's existing tests drive it (`PanelServiceHostTests.cs:195-203`).

## S1.8 — What S1 does not close, and what is reported upstream

- **U1 — settled 2026-11-16.** Epoch 1's parent said the release keeps `AwaitingResolve`. A hold whose round the caller
  already resolved has it `false` (F6), which left `AdvanceOnResolve` with no resolve to consume it: a later
  `call_human` round under a non-auto policy advanced on a plain `resolve` (T21 shows it), and a feature review held at
  its cap never closed. Reported from this spec, it became the parent's entry 1.2 and stopped epoch 1. The revised
  § 4.3 fixes the state — `AwaitingResolve = true`, `AdvanceOnResolve = true` — and says why the stage does not advance
  at once on release (it would bypass `Finish` from read paths such as `status`). § S1.3, D10, J11, T19–T21 and
  T56–T58 are written to it.
- **U2 — settled 2026-11-16, a reference repaired in the parent, not a change.** The parent named
  `RoundsDb.RecordRoundRow` as the writer of the note. That method is private, and its one caller, `RecordRound`,
  rewrites every column of the round's row from a `RoundContext` the release does not hold, re-inserts the reviewers
  and re-records the findings (F28). The parent's § 4.3 now says what § S1.5.1 builds: a one-column update of
  `rounds.note`, no schema step, the findings not re-recorded.
- **U3 — settled 2026-11-16 in the parent, not a change.** An answer found at the second read is applied as today, and
  for Discuss or words with no button today's rule keeps the hold — whose cards the release had withdrawn a moment
  before, so the person had nothing left to press. The parent's § 4.3 now says "as today" includes the card: the
  withdrawn cards are restored to the state they had (D16, `Escalations.Restore`, T50, T66, T67). Such a hold is the
  person's and answers `GateHeld`, and requirement 4 says so: "unless the person answered it first".
- **A release that could not be written.** Its four causes each have a reason, and a review entry says it
  (`NotWrittenSentence`); the session stays held on disk until the next deciding call. Handed to S3: `ask_human`'s reply
  for this case (parent § 4.4, first row). The `Release` tells it apart — its `NotWritten` is not empty — and
  `LegacyHold.Stands(session.State)` does not: since U3 a hold from before `auto` that the person answered stands as
  well, and that hold is the person's. S1 keeps the `Release` at `AskHumanService.cs:100` for that reason (X8), and
  the stories document's N3 has the three rows.
- **A card no session records.** A notice an older build wrote without recording it on the session is not withdrawn
  (parent § 4.3, *Residuals of the order*). The retention deletes it seven days after it was asked (F18).
- **A settings file that cannot be read means `auto`** — now the parent's own decision (§ 6), with its record (the
  note, D15), its second read and the rule that such a read releases nothing (§ S1.5.2). A reload that finds the file
  busy takes no default and marks nothing: the last good settings came from a read that succeeded.
- **The closing verdict of an epic.** A released code-stage hold advances on `resolve`, and `_cadence.Close`
  (`PanelService.cs:1792`) records the last round's verdict, `call_human` (T57 pins that it closes, once). S2 corrects
  `CadenceState`'s wording so that `call_human` with the release note reads as "released by auto" (parent § 4.2).
- **Step 3's "never holds"** is proved in S1 at the exhaustion site only (T24). The nobody-answered site is S2's.
- Handed to S3's document pass: one paragraph in `research/module_server.md` § *Persistence* — `autonomous` absent is a
  meaning, not a default, and a release that renames it must keep reading the old name.

## S1.9 — The defaults flip and the existing tests (D5)

**Step 1 — commit 2, before the flip, defaults unchanged.**

```bash
grep -lE 'HumanGate|call_human|GateHeld|HoldQuestions|CallHuman|AskHumanAsync|ask_human|new Escalations\(|no_answer_yet' src_mcp/tests/*.cs \
  | xargs grep -lE 'new PanelSettings\b|new PanelConfig\b|PanelConfig\.Uniform\(|FromEnvironment\(|Defaults\(\)' | sort
```

30 files at `1f0ae332` (the hold suites, the machine and gate tests, the `ask_human` suites, the end-to-end tests). In
each, every site that takes a default gets `human` named. Nothing else in the file changes.

| The site | Becomes |
|---|---|
| `PanelConfig.Uniform(a, b)` | `PanelConfig.Uniform(a, b, StagePolicy.Human)` |
| `new PanelConfig()` | `new PanelConfig(OnExhausted: StagePolicy.Human)` |
| `new PanelConfig(roles)` | `new PanelConfig(roles, StagePolicy.Human)` |
| `new PanelSettings { … }` with no `Rounds` | add `Rounds = new PanelConfig(OnExhausted: StagePolicy.Human),` |
| `Rounds = new() { … }` | `Rounds = new(OnExhausted: StagePolicy.Human) { … }` |
| `Defaults()` (`FakeCliRoundTests.cs:85-92`) used without its own `Rounds` | `Defaults() with { Rounds = PanelConfig.Uniform(3, 2, StagePolicy.Human) }` at the class's call — never in the shared base |
| `FromEnvironment(env)` whose `env` has no `COAI_ON_EXHAUSTED` | `human` for that key, the rest as it was |

A site that already names a policy, and `CodeRoundHarness.Service(...)` (its parameter already defaults to `Human`), are
left alone. Then: build, the whole suite green, and

```bash
git diff -U0 HEAD -- src_mcp/tests | grep -E '^[-+][^-+]' | grep -E '\.Should\(|Assert\.'     # prints nothing
```

**Step 2 — commit 4, the flip.** Run the whole suite. For each red test: read the failure. It is red only because a
default meant `human` when the message shows `good_enough` where `call_human` was expected, a hold that is not there, or
`GoodEnough` where `Human` was expected (for example `Forged` in `ASessionFileKeepsWhatANewerServerWroteTests.cs:328-335`,
whose `Baseline` then names `Human`). Apply the same table; assertions never change. Any other reason: stop and report.

**The PR lists** the files of step 1 and the tests of step 2. Every other test now runs under the shipped default. That
is the evidence for done-when 8: the hold suites ran green with `human` named before the flip and after it, and no
assertion line in them changed.

## S1.10 — Scenarios

Every scenario is written before its code. Where a test cannot compile, add the empty member first: `Autonomous` with
`private init` that nothing sets, `IsAutonomous` and `PredatesAuto`, a file constructor that chains to today's and sets
nothing, `UnderPolicyOf` returning `this`, `LegacyHold` with `Stands => false` and `Released` and `Stamped` returning
their input, `Withdraw` and `Restore` returning `Failed`, `SessionPolicy` whose methods return their input (as `new Release(session, "")`)
and whose `AwaitsItsResolve` is `false`, `LegacyHold`'s constants and two sentences as written, `RoundNotes.Write`
returning 0, `SettingsRead.Unusable` that nothing returns, `PolicyUnread` that nothing sets, `StartLayer` answering
`Layer`'s lookup with no reason. **RED** is the failure seen with those stubs (or, where a commit is named, at that commit). **GUARD** is a test that is green before the code because
it pins something the code must not break; its break-it is its proof.

### S1.10.1 — The words: `UnknownSettingValueTests` (existing) and `PanelConfigPolicyTests` (new, pure)

| # | Test | Setup and action | Expected | First run | Break-it |
|---|---|---|---|---|---|
| T1 | `UnknownSettingValueTests.AbsentEmptyAndAuto_ArePolicyAuto` (Theory: absent, `""`, `auto`, `AUTO`, `Auto`) | `PanelSettings.FromEnvironment(With(value))` | `Rounds.OnExhausted == GoodEnough`, `Rounds.Autonomous == true`, `IsAutonomous`, `Unrecognised` empty | RED: *Expected … GoodEnough, but found Human*; for `auto` also one complaint | remove `or "auto"` from `PolicyOf` → the three `auto` rows: `Human` and a complaint |
| T2 | `…EveryPolicyThisBuildKnows` (existing Theory, two assertions added) | the five rows it has | also `Rounds.Autonomous == false` and not `IsAutonomous` | RED: *Expected false, found <null>* | write `Autonomous = true` in the code constructor → *found True* |
| T3 | `…AWordThatOnlyLooksLikeAuto_StillStops` (Theory: `" "`, `"auto "`, `" auto"`, `automatic`, `auto,human`) | the same | `Human`, `Autonomous == false`, one complaint that contains the value and "asking a person" | GUARD for the policy and the complaint (the `Autonomous` assertion goes green with T2) | add `.Trim()` in `PolicyOf` → `"auto "` reads as auto |
| T4 | `PanelConfigPolicyTests.NoPolicyNamed_IsPolicyAuto_WhereverADefaultIsBuilt` (Theory) | `new PanelConfig()`, `new PanelConfig(roles)`, `new PanelConfig { Catalog = … }`, `PanelConfig.Uniform(1, 6)`, `new PanelSettings().Rounds`, `PanelSettings.FromEnvironment(_ => null).Rounds` | each `GoodEnough` with `Autonomous == true` | RED at commit 4: *found Human* | `?? StagePolicy.Human` in the code constructor |
| T5 | `…APolicyNamed_IsThatPolicy_AndNeverAuto` (Theory over `Enum.GetValues<StagePolicy>()` × positional, `OnExhausted:` named, `Uniform(3, 2, p)`) | — | that policy; `Autonomous == false`, never `null`; not `IsAutonomous`; not `PredatesAuto` | RED: *found <null>* | `Autonomous = OnExhausted is null ? true : null` |
| T6 | `…OnlyTheFilesConstructor_LeavesTheMarkerAbsent` | `new PanelConfig(roles: null, autonomous: null)`; the same with `false`; with `true, StagePolicy.GoodEnough`; and `typeof(PanelConfig).GetProperty(…)!.SetMethod!.IsPrivate` for `Autonomous` and `OnExhausted` | `null` + `PredatesAuto` + `Human` + every shipped role; `false`; `true`; both setters private | RED: the `false` and `true` rows read `<null>` (the stub sets nothing) | make `Autonomous`'s `init` public → the setter row fails |
| T7 | `…UnderPolicyOf_TakesBothHalves_AndKeepsTheRest` (Theory: target from the file constructor with `null`, `Human` named, auto × current auto and each named policy) | `target.UnderPolicyOf(current)` | `(current.OnExhausted, current.IsAutonomous)`; `Roles`, `SecurityLane`, `Unknown`, `Catalog` the same references; never `null`; `IsAutonomous` ⇒ `GoodEnough` on every result | RED: the pair is the target's | drop `Autonomous = …` from the `with` |

### S1.10.2 — The file: `ASessionFileKeepsWhatANewerServerWroteTests` (existing; its `Plant`, `Edit`, `Spelled`)

| # | Test | Setup and action | Expected | First run | Break-it |
|---|---|---|---|---|---|
| T8 | `AutonomousAbsentFalseAndTrue_ComeBackFromALoadAndASave_AsTheyWere` (Theory: absent/`Human`, `false`/`Human`, `false`/`GoodEnough`, `true`/`GoodEnough`) | `Plant(store, [])`; `Edit`: set `state.config.onExhausted`, set or remove `state.config.autonomous`; then `store.Save(store.Load(…)!)` twice | `Spelled(file, ["state","config"], "autonomous")` is 0 for absent and 1 otherwise, with the same value; `onExhausted` the same word; `sessionId` and the one round kept (not a new session) | **GREEN at commit 1, on purpose**: that tree is a build before `auto` (D2). Record the sha and the runner's line | at commit 1: remove `[JsonExtensionData, JsonInclude]` at `SessionState.cs:156` → the `false` and `true` rows: *Expected 1, found 0* (residual A9). After commit 3: drop `Autonomous = autonomous;` from the file constructor → the same rows |
| T9 | `AFileWithNoAutonomous_ReadsAsNull` (Theory: `Human`; `GoodEnough`; `"autonomous": null`) | `Edit` as above; `store.Load` | `Config.Autonomous is null`, `PredatesAuto`, not `IsAutonomous`, `Config.Unknown` null, the policy as stored | GUARD (input 1's test) | give the file constructor `bool? autonomous = false` → *found False* |
| T10 | `TheNaivePositionalDefault_WouldReadAbsenceAsAuto_AndThatIsThePoint` | a test-local `record NaiveConfig(StagePolicy OnExhausted = StagePolicy.GoodEnough, bool? Autonomous = true)` and a test-local `JsonSerializerContext` with the session options; deserialise `{"onExhausted":"Human"}` | `Autonomous == true` — asserted: the defect the file constructor exists to avoid (M1) | GUARD | — (it shows a refuted shape, `testing.md` § *give a refuted approach a shape*) |
| T11 | `AStoredMarker_BindsToTheMember_AndNeverToTheBag` (Theory: `"autonomous": false`, `true`, `"AUTONOMOUS": false`) | `Edit`; `Load`; `Save` | the value; `Config.Unknown` null; after the save `Spelled == 1` | RED: *found <null>* | name the file constructor's parameter `auto` → the load throws ("must bind") |
| T12 | `AComputedName_IsNotTheMarker` | `Edit`: remove `autonomous`, add `"isAutonomous": true, "predatesAuto": false`; `Load`; `Save` | `Autonomous is null`; neither name in the bag; neither spelled in the file after the save | GUARD | remove `[JsonIgnore]` from `IsAutonomous` → it is written |
| T13 | `AKnownMarkerInABag_IsDroppedOnSave_AndTheTypedValueWins` | (a) a loaded config with `false`, its bag given `autonomous: true` (`WithEntry`, `:289-290`) — reached through `loaded.State.Config with { Unknown = … }`; (b) a config with `null`, the same bag; `Save` | (a) `Spelled == 1`, value `false`; (b) `Spelled == 0` — a bag cannot make the marker | RED (a): *Expected 1, found 0* | — (the scrub is by contract names; `[JsonIgnore]` on `Autonomous` breaks T11 first) |
| T14 | `AFileWithNoConfig_IsAFileFromBeforeAuto` | `Edit`: remove `state.config`; `Load` | `PredatesAuto`; `OnExhausted == Human`; a gate for every `PanelConfig.AllRoles`; the live catalog | RED at commit 3: *Expected <null>, found False* | `SessionStore.cs:704` back to `new PanelConfig { Catalog = live }` |
| T15 | `AConfigThisBuildMade_IsNeverSavedWithoutTheMarker` (Theory: `new()`, each named policy, `Uniform(3, 2)`, `Uniform(3, 2, Human)`, `FromEnvironment(absent).Rounds`, `FromEnvironment(human).Rounds`, a file-constructor config `.UnderPolicyOf(new())`) | save a session with it | `Spelled == 1`, and the value is the config's | RED: *Expected 1, found 0* | — (covered by T5's) |
| T16 | `EveryBaggedTypesMembers_AreTheListedOnesOrRetired` (existing) | — | green once the three names are in the list (`:408`) | RED when the members exist: *PanelConfig.autonomous is new and is not in the committed list* | — |
| T17 | `AMarkerOfAnotherType_IsNotReadAsASession` | `Edit`: `"autonomous": "false"`; `Load` | `null` — the file is not read at all (F13), so no release decides on half of it | GUARD | — (pins the serializer's strictness this story relies on) |

### S1.10.3 — The pure hold: `LegacyHoldTests` (new, `src_mcp/tests/LegacyHoldTests.cs`)

Helpers: `Before(…)` — a `SessionState` whose config is `new PanelConfig(roles: null, autonomous: null)`; `Mine(policy)` —
`PanelConfig.Uniform(1, 0, policy)`; `Auto` — `PanelConfig.Uniform(1, 0)`; `Nobody` as in the stories document § 8.2.

| # | Test | Setup and action | Expected | First run | Break-it |
|---|---|---|---|---|---|
| T18 | `Stands_OnlyForAHeldSessionWhoseConfigPredatesAuto` | held × not held × marker `null`, `false`, `true`; and held with every config T15 lists | true for (held, `null`) only | RED: *Expected True* | `Stands => s.HumanGate` → the `false` and `true` rows |
| T19 | `Released_LeavesTheSessionAsAnAutoVerdictWould` (Theory: the held round resolved or not) | `Before` held, `RoundsRunThisStage = 1`, `EscalationsUsed = 2`, `HoldQuestions = ["n-1", "q-2"]`, one rejection; `LegacyHold.Released(s, Auto)` | `HumanGate` false; `HoldQuestions` empty; `AwaitingResolve` and `AdvanceOnResolve` true; config `GoodEnough`/`true`; the three counters and the rejection unchanged | RED: *Expected HumanGate false* | drop `HoldQuestions = []` |
| T20 | `AReleasedHold_ThenAnEmptyResolve_AdvancesTheStage` (Theory: plan, code, document, a feature session with two rounds run × resolved or not) | `Released`, then the stage's begin, then `RoundMachine.Resolve(state, [])`, then `Resolve` again | the begin is `Refused(Unresolved)` — the MACHINE's sentence; the engine answers the release's own before the machine is asked (T54); the first resolve is `Moved` to the stage's `AdvancesTo` (`PlanProceeded` for a plan), `AdvanceOnResolve` and `AwaitingResolve` false after it; the second is `Refused` | RED: the resolve does not advance, and a resolved hold's begin is `Refused(GateHeld)` | `AdvanceOnResolve = false` in `Released` → no advance; `AwaitingResolve = s.AwaitingResolve` → the resolved rows are `Refused` |
| T21 | `TheNaiveRelease_ThatKeepsAwaitingResolveFalse_WouldLetALaterCallHumanRoundAdvance_AndThatIsThePoint` | by hand: a resolved held session `with { HumanGate = false, HoldQuestions = [], AdvanceOnResolve = true }`, config `UnderPolicyOf(Mine(Human))`; `CompleteRound(s, GateResult.Empty, Nobody)`; `Resolve(state, [])` | the verdict is `CallHuman` and the resolve MOVES to `CodeReview` with `HumanGate` still true — asserted, as the defect the parent's entry 1.2 names (U1) | GUARD | — (a refuted shape) |
| T22 | `Stamped_FollowsTheSettings_ExceptWhileAHoldFromBeforeAutoStands` | (not held, `Before`) + `Auto`; + `Mine(Human)`; (held, `Before`) + `Auto`; + `Mine(Human)`; (held, `Mine(Human)`) + `Auto`; + `Mine(Escalate)` | `GoodEnough`/`true`; `Human`/`false`; the SAME reference; the SAME reference (D8); `GoodEnough`/`true`; `Escalate`/`false` | RED: rows 1, 2, 5, 6 return the input | `Stamped => s.Config.UnderPolicyOf(current)` → rows 3 and 4 |
| T23 | `ARoundNeverRunsOnAConfigWhoseMarkerIsAbsent` | every begin of `RoundMachine` (plan, code, code again, document, feature, feature again) on a held `Before` session of its kind; and `Stamped` on a `Before` session that is not held | every begin `Refused`; the stamped config is not `PredatesAuto` | RED on the second half | as T22 |

### S1.10.4 — The refresh: `ASessionFollowsTheCurrentPolicyTests` (new, `[Collection("fakecli-env")]`)

| # | Test | Setup and action | Expected | First run | Break-it |
|---|---|---|---|---|---|
| T24 | `ASessionOpenedUnderHuman_EndsItsNextRoundUnderAuto` (on `CodeRoundHarness`, which exercises C5) | `Service(threshold: 0, onExhausted: Human, maxRounds: 1)`: `OpenAsync`. Then, same harness, `Service(threshold: 0, maxRounds: 1, autonomous: true)`: one finding over the threshold, `ReviewPlanAsync` | `good_enough`; `HumanGate` false; no file under `<data>/escalations`; the file's config is `GoodEnough` with `autonomous: true` | RED: `call_human` — the session kept `Human` | drop `LegacyHold.Stamped` at `RoundEngine.cs:279` |
| T25 | `TogglingThePolicy_DoesNotRefillTheLadder` (`FakeCliRoundTests`) | a session of two rounds per stage, threshold 0, opened under `Escalate`; one finding every round, each resolved with `accept`. Rounds 1–2 under `Escalate` (the second answers `escalated`); round 3 under auto (`Uniform(2, 0)`); round 4 under `Escalate` again | after round 3: the file's config is `GoodEnough`/`true` and `escalationsUsed == 1`; round 4 answers `escalated` and `escalationsUsed == 2`. If the sequence of verdicts differs, keep the two claims: the stored pair follows the settings, and `escalationsUsed` never goes down | RED: after round 3 the config is `Escalate` | — (`Stamped` returns a config; it cannot reach the counter) |
| T26 | `AHoldRaisedUnderHuman_OnAFileFromBeforeAuto_IsSavedWithTheMarker` (`FakeCliRoundTests`) | open under `Uniform(1, 0, Human)`; `StripTheMarker` (§ S1.10.6) on the not-held file; a round with one blocking finding | `call_human`; the file's config has `"autonomous": false`. From commit 7 on, also: `status` under auto leaves it held, the notice open | RED: no `autonomous` in the file | as T24 |

### S1.10.5 — The withdrawal: `EscalationsTests` (existing) and `SessionPolicyTests` (new, no CLI, `TempDir.For`)

| # | Test | Setup and action | Expected | First run | Break-it |
|---|---|---|---|---|---|
| T27 | `EscalationsTests.Withdraw_TakesAnOpenQuestionOutOfTheActiveSet` | `Notify(Question("qW"))`; `Withdraw("qW", when)` | `Withdrawn`; `Status == expired`; `ExpiredUtc == when`; the question's text kept | RED: *Expected Withdrawn, found Failed* | — |
| T28 | `…Withdraw_ChangesNothingTheSecondTime_AndSaysNotThere` | the same id again with a later time; an id with no file; a file that holds `not json` | `NotThere` three times; `ExpiredUtc` still the first; the third file's bytes unchanged | RED | rewrite an expired card → `ExpiredUtc` moves |
| T29 | `…Withdraw_ThatCannotWrite_SaysFailed_AndLeavesTheCardOpen` (Theory: `IOException`, `UnauthorizedAccessException`) | written by a working instance; `new Escalations(dir) { Writer = (_, _) => throw … }.Withdraw(…)` | `Failed`; the card still open | GREEN by the stub's accident (the stub answers `Failed`); its break-it is its proof | answer `NotThere` in the catch |
| T30 | `SessionPolicyTests.TheNoticesAreWithdrawnBeforeTheSessionIsSaved_AndAFailedSaveIsRetried` (F1) | a held session from before `auto` with notice `n-1`, planted with a working store; a `SessionStore` whose `FileWriter` throws `IOException`; auto settings; `Released(session)`. Then a policy over a working store: `Released(store.Load(…)!)` | first: the returned session still held with `["n-1"]`, `NotWritten == LegacyHold.SessionNotSaved`; on disk still held, no note on its round; `n-1` expired at `t1`; one Warning "could not save the release". Second: `NotWritten` empty; on disk `HumanGate` false; `n-1` still expired with `ExpiredUtc == t1`; one Warning that contains "auto released the" | RED: nothing is released | save before the withdrawal → after the first call `n-1` is still open |
| T31 | `…ANoticeThatCannotBeWithdrawn_KeepsTheHold_AndTheNextLoadReleasesIt` (F1, the cadence consultation) | notices `n-1` and `q-2`, both open; an `Escalations` whose `Writer` throws for `n-1` only; `Released`. Then working | first: the SAME session object, `NotWritten == LegacyHold.NoticeNotWithdrawn`; on disk held with `["n-1", "q-2"]`; `n-1` open, `q-2` expired; a Warning that names `n-1`; no "auto released". Second: released; `n-1` expired; `q-2`'s `ExpiredUtc` unchanged | RED | treat `Failed` as done → the first call releases with `n-1` open |
| T32 | `…Released_WritesNothing_ForAnyOtherSession` | (before-auto hold, `Human` settings); (this build's hold — `Uniform(1, 0, Human)` — auto settings); (before-auto file, not held, auto) | the same object each time, `NotWritten` empty; the session file's bytes and every card unchanged; no Warning | GUARD | `Stands => s.HumanGate` → row 2 is released |
| T33 | `…CurrentUnclaimed_ReleasesUnderTheSessionsClaim_AndReadsAgainUnderIt` | (a) the test holds `SessionClaim.TryTake(…)`; `CurrentUnclaimed`. (b) the claim free. (c) a STALE held copy passed in while the file on disk is already released | (a) a held session back with `NotWritten == LegacyHold.ClaimBusy`, nothing written; (b) released, `NotWritten` empty, and `SessionClaim.TryTake` succeeds afterwards; (c) the disk's session back, nothing written, no "auto released" | RED (b) | remove the `TryTake` → (a) writes; remove the second load → (c) saves the stale copy |

### S1.10.6 — Whichever call comes first: `AHoldFromBeforeAutoIsReleasedTests` (new, `FakeCliRoundTests`, `[Collection("fakecli-env")]`)

The fixture is a hold the REAL code raised, then made a hold from before `auto` by editing its file — never a session
assembled by hand. Copy `HoldingService`, `HeldAsync`, `NoticeId`, `NoticeFile`, `ThePersonAnswers`, `RejectIt` and
`OneBlockingFinding` from `OnlyAPersonReleasesTheGateTests.cs:29-90` (`Uniform(1, 0, StagePolicy.Human)`, a 300 ms
escalation budget). Add:

- `StripTheMarker(bool recorded)` — in the session file (`Store.FileFor`), remove `state.config.autonomous`; when
  `recorded` is false also set `state.holdQuestions` to `[]`, as an older build left it
  (`AFeatureIsReviewedEndToEndTests.cs:1135`). Then assert the fixture as the real loader reads it:
  `Session().State.Config.PredatesAuto` (and `HumanGate`, where the test planted a hold).
- `AutoService(ILogEventSink sink)` — `new PanelService(Defaults() with { Rounds = PanelConfig.Uniform(1, 0), CadenceMode = Off, SplitPlan = false, EscalationBudget = 300 ms }, VaultKeys.None(…), default, _launcher, <a logger over the sink, as RoundAuditTests builds it>, Noticing.None)`.
- `Released()` — the assertions shared by T34–T38: the file has `HumanGate` false, no hold questions,
  `AdvanceOnResolve` and `AwaitingResolve` true, config `GoodEnough`/`true`; when `recorded`, the notice file's `status` is
  `expired`; when not, the number of files under `escalations/` did not grow (nothing re-issued, D4); exactly one Warning
  that contains "auto released the" and the session id; the file's last round keeps its verdict `call_human` and its
  `note` is `LegacyHold.Note`, and `coai.db`'s `rounds.note` for that round is the same (read as
  `ARoundsNumberComesFromTheJournalTests.cs:141-152` reads the database).

| # | Test (each a Theory over `recorded`) | Action after `HeldAsync`, `StripTheMarker`, under `AutoService` | Expected beside `Released()` | First run | Break-it |
|---|---|---|---|---|---|
| T34 | `AReviewEntry_ComesFirst` | `resolve(RejectIt)` under `human` BEFORE the strip (the hold stays, the round is resolved); then `ReviewPlanAsync` | the error is `LegacyHold.ReleasedSentence` for the plan stage, and contains neither "call_human", "a person has to decide" nor "have not been resolved"; then `ResolveAsync("[]")` answers stage `CodeReview` | RED: the error is `GateHeld`; the file is held | `RoundEngine.cs:267` back to `ApplyAnyHumanDecision(session)`; and, for D4, `Released` called AFTER `ApplyAnyHumanDecision` → the not-recorded row finds a re-issued notice |
| T35 | `Resolve_ComesFirst` | `ResolveAsync(RejectIt)` | stage `CodeReview`, `PlanProceeded` | RED: stage `PlanReview`, the file held | drop `_policy.Current` in `ResolveUnderClaim` |
| T36 | `Status_ComesFirst` | `StatusAsync` | `awaitingResolve: true`; no error | RED: the file held | drop the call at `PanelService.cs:567` |
| T37 | `AskHuman_ComesFirst` | `AskHumanAsync(_repo, Branch, "May I ship?")` | it returns; the reply's `status` is not `no_answer_yet`; no new file under `escalations/` | RED: a second question file, `no_answer_yet` after 300 ms, the file held | drop the call at `AskHumanService.cs:100` |
| T38 | `AFreshDocumentReview_ComesFirst` | the arrangement of `ADocumentIsReviewedEndToEndTests.ANewReviewOfAHeldDocument_IsRefused_AndTheHoldStays` (`:638`), the document session stripped; then `review_document` with `newReview: true` | the reply is `LegacyHold.ReleasedSentence` for the document stage — not `GateHeld`, and not `DocumentSessions.StillOpen`, whose words say nobody decided; after `resolve` of the document a fresh review starts | RED: `GateHeld` | drop the call in `WhyNotAFreshReview`; drop its `AwaitsItsResolve` arm → `StillOpen` |

### S1.10.7 — A hold this build raised: `AHoldThisBuildRaisedIsNotReleasedByAutoTests` (new, the same base and helpers)

Its fixture is `HeldAsync` with NO strip; it first asserts the file's config has `"autonomous": false`. `Kept()`:
`HumanGate` true, the notice id still among `HoldQuestions`, the notice file open, no log line that contains
"auto released".

| # | Test | Action under `AutoService` | Expected | First run | Break-it |
|---|---|---|---|---|---|
| T39 | `NoEntryReleasesIt` (Theory: a review entry — the round resolved first; `resolve`; `status`; `ask_human`) | the entry | `Kept()`; the review entry answers `GateHeld` | GUARD — and the same fixture, stripped, is released in T34–T37: the positive beside the negatives | `Stands => s.HumanGate` → every row |
| T40 | `AFreshDocumentReview_DoesNotReleaseIt` | T38's arrangement, not stripped | `Kept()`; the reply is `GateHeld` | GUARD | the same |
| T41 | `ResolveWithHumanDecisionProceed_IsStillRefused_UnderAuto` | `ResolveAsync(RejectIt, callerClaimsProceed: true)`; and with `"[]"` | an error that contains "Proceed despite the findings"; `Kept()`; stage `PlanReview` | GUARD | — (`ClaimRefusal` is not this story's; the test proves the setting does not reach it) |
| T42 | `ThePersonsProceed_StillReleasesIt_UnderAuto` | `ThePersonAnswers(NoticeId(), "proceed")`; `ResolveAsync(RejectIt)` | stage `CodeReview`; no "auto released" line | GUARD | — |
| T43 | `WhateverMakesThePolicyAuto_ReleasesOnlyAHoldFromBeforeAuto` (Theory: `Rounds` from `FromEnvironment(With("auto"))`, from `FromEnvironment(_ => null)`, from `FromEnvironment(With(""))`, from `Uniform(1, 0)` × stripped or not) | `StatusAsync` | stripped → `Released()`; not stripped → `Kept()` | RED for the stripped rows | as T39 |
| T44 | `AHoldFromBeforeAuto_ReadUnderHuman_KeepsItsMarker_AndAutoReleasesItLater` (F0; Theory: a review entry, `status`, `resolve(RejectIt)`, `ask_human`) | stripped; the entry under the HUMAN service; then `StatusAsync` under auto | after the first call: held, and the file still has no `autonomous`; after the second: `Released()` | RED: nothing releases. With an unconditional stamp the `resolve` row saves `"autonomous": false` and the second call keeps the hold | `Stamped => s.Config.UnderPolicyOf(current)` → the `resolve` row |
| T45 | `AHoldRaisedAfterAPersonReleasedTheOldOne_IsThisBuilds` (F0's other side) | the held round resolved with `accept` (a rejection would stand and the next round would pass); stripped, recorded; `ThePersonAnswers(NoticeId(), "continue")`; under HUMAN a new round whose blocking finding still gates → `call_human` again; then `StatusAsync` under auto | the file's config has `"autonomous": false`; a new notice id; `Kept()` for it | RED: the file has no `autonomous` (and, with the release in place, the new hold is released) | stamp before `ApplyAnyHumanDecision` instead of at `:279` |
| T46 | `AHoldFromBeforeAutoWithNoQuestion_IsReissuedUnderHuman_AndAutoWithdrawsThatNotice` (F0 with D4) | stripped, not recorded; under HUMAN `ReviewPlanAsync` (the round resolved first); then `StatusAsync` under auto | first: `GateHeld`, one NEW notice id on the hold, still no `autonomous`; second: `Released()`, and that notice expired | RED: the second call releases nothing | in `WithTheHoldReissued`, rebuild the config (`new PanelConfig(…)`) instead of copying the session → the re-issue writes a marker and the second call keeps the hold |

### S1.10.8 — The bench: `src_bench/CoaiBench.Tests/SettingsAppliedTests.cs`

`Config(…)` (`:19-23`) gains an optional `bool? autonomous`; absent writes no member.

| # | Test | Rows: asked · `onExhausted` · `autonomous` → | First run | Break-it |
|---|---|---|---|---|
| T47 | `TheAskedPolicy_AgainstWhatTheSessionStored` (Theory) | (1) `auto`·`GoodEnough`·true → ok; (2) `AUTO`·`GoodEnough`·true → ok; (3) `auto`·`GoodEnough`·false → mismatch; (4) `auto`·`GoodEnough`·absent → mismatch; (5) `auto`·`Human`·absent → mismatch; (6) `good_enough`·`GoodEnough`·false → ok; (7) `good_enough`·`GoodEnough`·absent → ok; (8) `good_enough`·`GoodEnough`·true → mismatch; (9) `human`·`Human`·false → ok; (10) `human`·`Human`·absent → ok; (11) `human`·`Human`·true → mismatch; (12) `human`·`GoodEnough`·true → mismatch; (13) `continue`·`Continue`·false → ok; (14) `escalate`·`Escalate`·absent → ok; (15) `escalate`·`Escalate`·true → mismatch. Every mismatch names the asked word and what the session says; "on-exhausted" is in `Checked` for every row | RED: rows 1 and 2 (*asked 'auto', session says 'GoodEnough'*) and rows 8, 11 and 15 (no mismatch reported). The other rows are green before | compare the word only for `auto` → rows 3 and 4; drop the `true` test for the other words → rows 8, 11, 15 |
| T48 | `ASessionThatRunsAuto_IsNotJudgedWhenNothingWasAsked` | no `COAI_ON_EXHAUSTED` asked · `GoodEnough` · true → `Ok`, and "on-exhausted" is not in `Checked` | GUARD | — |

The three existing tests of that file stay as they are and stay green (a config with no `autonomous` is an older server's).

T49–T67 are the parent's epoch 2. `SessionPolicyTests` builds its policy with `new Store.Projection(dir, log)` over the
test's own folder from here on (T30–T33 too).

### S1.10.9 — The person's answer wins: `SessionPolicyTests`, `AHoldFromBeforeAutoIsReleasedTests` (D11, J9)

| # | Test | Setup and action | Expected | First run | Break-it |
|---|---|---|---|---|---|
| T49 | `SessionPolicyTests.AnAnswerOnFile_IsNotOverriddenByARelease` (Theory: `continue`, `fix`, `discuss`, `proceed`, words with no button — on the notice; and words on a second question of the hold) | a held session from before `auto` with notice `n-1` (kind notice, its `decisions` listed) and question `q-2`; the answer file written BEFORE; auto settings; `Released(session)`, and `CurrentUnclaimed` | the same object, `NotWritten` empty; `n-1` and `q-2` still open; the session file's bytes unchanged — no marker, no note; no line that contains "auto released"; `CurrentUnclaimed` takes no claim | GREEN by the stub's accident (the stub returns its input); its break-it is its proof | drop step 2 of `Released` → the hold is released over the answer and both cards are expired |
| T50 | `AHoldFromBeforeAutoIsReleasedTests.AnAnswerWrittenBetweenTheWithdrawalAndTheSecondRead_Wins` (Theory: `continue`, `proceed`, `discuss`) — the race | `HeldAsync`, the held round resolved under `human` (`RejectIt`), `StripTheMarker(recorded: true)`. The race: a `SessionPolicy` built by hand over the fixture's folder with `new Escalations(dir) { Writer = <records every (path, text) it is given, writes it with AtomicJson.Write, and after its FIRST write also writes the answer file of that question, as ThePersonAnswers writes it> }`; `Released(Session())`. Then the entry, under `AutoService` | from `Released`: the same object, `NotWritten` empty; the seam saw the notice's file written exactly twice — first with `"expired"`, then open; the notice reads back as before the call (`Status` and `ExpiredUtc` empty; its question, kind and decisions the same); the file still held with no marker and no note; one Information line that contains "their answer stands" and "offered again"; no "auto released". Then — `continue`: `ReviewPlanAsync` runs a round (a `verdict`, not an error), with the line "a person chose"; `proceed`: `ResolveAsync("[]")` answers stage `CodeReview`; `discuss`: `ReviewPlanAsync` answers `GateHeld`, the hold stays and its notice is open — the person can answer again; a `continue` then written with `ThePersonAnswers` lets the next `ReviewPlanAsync` run. In every row no round of the file carries `LegacyHold.Note` and `coai.db` holds no such note | RED: the seam saw no write (the stub withdraws nothing, so the answer is never written) and no line says "their answer stands" | drop the second read in step 5 → the release is saved over the answer: the note is on the round, "auto released" is logged, and the `discuss` row is no longer held. Drop the restore in step 5 → the seam saw one write and the `discuss` row's notice is `expired` |
| T66 | `EscalationsTests.Restore_PutsAWithdrawnCardBackAsItWas_AndChangesNothingTheSecondTime` | `Notify` a notice (kind notice, decisions listed) and read it; `Withdraw(id, t1)`; `Restore(id)`; `Restore(id)` again; then: an id with no file; a file that holds `not json`; a card `Expire`d at `t0` and then restored | `Restored`, and the card reads back member for member as the first read (`Status` and `ExpiredUtc` empty); then `NotExpired` with the file's bytes unchanged; `NotExpired` for the missing and the malformed file, bytes unchanged; the `Expire`d card: `Restored` — the method does not know who expired a card, its callers do (D16). No answer file is created or touched | RED: *Expected Restored, found Failed* (the stub) | write only `Status` → `ExpiredUtc` still reads `t1` |
| T67 | `SessionPolicyTests.ARestoreThatFails_IsAWarning_AndTheNextLoadOffersTheNoticeAgain` | notice `n-1` and an `ask_human` question card `q-2` of the hold, both open; `q-0`, a question card of the hold already expired at `t0`. The `Writer` writes the two withdrawals, writes the answer (`discuss`) after the first, and THROWS for every later write; `Released`. Then a policy over a working `Escalations`: `Released` twice | first: the same object, `NotWritten` empty; `n-1` and `q-2` still `expired`; one Warning that contains "could not offer" and names both; nothing saved. Second: `n-1` open again (`Restored`), one Information line that contains "offered again"; `q-2` stays `expired` (a question card is not retried); `q-0` still expired at `t0`, in both calls; the hold stands, no note, no "auto released". Third: no write at all | RED: no Warning — the stub withdraws and restores nothing | treat `Failed` as `NotWritten` → the first call answers a reason although the person's answer stands; drop the retry in step 2 → `n-1` stays expired for ever; restore every expired card of the hold in step 2 → `q-0` is opened |

### S1.10.10 — The note: `SessionPolicyTests`, `RoundNotesTests` (new, `src_mcp/tests/RoundNotesTests.cs`) (D12, U2)

| # | Test | Setup and action | Expected | First run | Break-it |
|---|---|---|---|---|---|
| T51 | `SessionPolicyTests.TheReleaseIsNotedOnTheHeldRound_InTheFileAndInTheDatabase` | a held session from before `auto` with two rounds — round 1 `revise`, round 2 `call_human` — each recorded through the real `RoundsDb.RecordRound` with a `RoundContext` (plan text `PLAN`, a sha, a caller) and round 2 with one finding whose decision is recorded (`RecordDecisions`, a reasoned reject); `Released` | the file: `Rounds[^1].Note == LegacyHold.Note`, its verdict still `call_human`, `Rounds[0]` unchanged. The database: `rounds.note` of round 2 is the note, of round 1 empty; round 2's `plan_text`, `head_sha`, `caller`, `verdict`, `rejected` count, its reviewers and its finding's `resolution` are what they were | RED: the note is empty in both places | project through `db.RecordRound(state, round, [])` instead → `plan_text` reads empty and the finding is gone — the reason for U2 |
| T52 | `RoundNotesTests.Write_ChangesOneColumnOfOneRow_AndCreatesNoRow` | rows for (`s`, `PlanReview`, 1) and (`s`, `PlanReview`, 2), seeded as T51 seeds them; `Write` for round 2; the same again; `Write` for (`s`, `CodeReview`, 1), which is not there | 1; 1, and the note is the same; 0, and the table still has two rows. Round 1's row is byte for byte what it was (every column read back) | RED: 0, and the note is empty | drop `AND number = $number` → round 1's note changes |
| T53 | `SessionPolicyTests.ARelease_NeedsNeitherARoundNorTheDatabase` | (a) a held session from before `auto` whose `Rounds` is empty; (b) a session with a round, and `coai.db` in the folder replaced by a DIRECTORY of that name before the call; `Released` each | (a) released and saved, `NotWritten` empty, no exception; (b) no exception, `NotWritten` empty, released on disk with the note on its round, and the "auto released" Warning is still written — whatever the projection logged about itself | RED: nothing is released | call the projection before `store.Save` and return on its failure → (b) stays held |

### S1.10.11 — What a review entry answers: `AHoldFromBeforeAutoIsReleasedTests` (D13)

The holds of the four stages are raised by the real code, as T56 arranges them.

| # | Test | Setup and action | Expected | First run | Break-it |
|---|---|---|---|---|---|
| T54 | `AfterARelease_EveryReviewEntry_NamesTheReleaseAndResolve` (Theory: `review_plan`, `review_code`, `review_document`, `review_feature`, each on a hold of its own stage × the release written by this call, or by an earlier `StatusAsync`) | stripped, recorded, under `AutoService`; the entry. And one row that was never held: under `AutoService` a plan round that answers `revise`, then `ReviewPlanAsync` again with no resolve | the error is `LegacyHold.ReleasedSentence(<the stage's phrase>)`, exactly; it contains "call resolve" and none of "have not been resolved", "call_human", "a person has to decide", "nobody has decided"; when the release was earlier, the refusing call changes no byte of the session file; a second identical call answers the same. The never-held row answers `RoundMachine.Unresolved`, as today | RED: `GateHeld` (the never-held row is a GUARD) | drop the `AwaitsItsResolve` check in `RunStageAsync` → `RoundMachine.Unresolved`; make `AwaitsItsResolve` ignore the note → the never-held row answers the release sentence |
| T55 | `AReleaseThatCouldNotBeWritten_IsSaidWithItsReason_AndNeverAsGateHeld` (Theory: (a) `review_plan`, the notice's question file replaced by a DIRECTORY of its name, so `Withdraw` answers `Failed`; (b) a fresh document review while the test holds the held document session's `SessionClaim`) | stripped, recorded, under `AutoService`; the entry. Then the obstacle removed — the question file put back as it was, the claim disposed — and the entry again | first: the error is `LegacyHold.NotWrittenSentence(stage, LegacyHold.NoticeNotWithdrawn)` for (a), `… ClaimBusy` for (b), exactly; it contains "call again" and neither "a person has to decide" nor "ask_human"; the file is held with no marker and no note; no new file under `escalations/`. Second: `LegacyHold.ReleasedSentence`, and `Released()` | RED: `GateHeld` | drop the `NotWritten` return in `RunStageAsync` → `GateHeld`; for (b) drop its arm in `WhyNotAFreshReview` → `GateHeld` |

### S1.10.12 — One resolve after a release: `AReleasedHoldAdvancesOnceTests` (new, `FakeCliRoundTests`, `[Collection("fakecli-env")]`) (D10, J11)

`PanelService` level, every hold raised by the real code under `human`: the plan hold as `HeldAsync`; the code hold after
a plan stage that proceeded, by a code round whose finding gates with the budget spent (`CodeRoundHarness`); the
document hold as `ADocumentIsReviewedEndToEndTests.cs:638` arranges it; the feature hold at its cap as
`AFeatureIsReviewedEndToEndTests.cs:607-615` (round 1 `revise`, resolved, round 2 `call_human`). *Resolved* means: before
the strip, under `human`, `ResolveAsync` with a reasoned reject of the open finding — the hold stays and
`AwaitingResolve` is false (F6). *Not resolved* means the finding is still pending. **`PanelService` refuses a `resolve`
with no decisions while findings are pending (F27), so in the not-resolved rows the caller passes its decisions; `[]`
is the resolved rows' call.**

| # | Test | Setup and action | Expected | First run | Break-it |
|---|---|---|---|---|---|
| T56 | `OneResolveAfterARelease_AdvancesTheStageExactlyOnce` (Theory: plan, code, document, feature × resolved, not resolved) | the hold; `StripTheMarker(recorded: true)`; under `AutoService` `StatusAsync` (the release). Not resolved: `ResolveAsync("[]")` first, then `ResolveAsync(<a reasoned reject>)`. Resolved: `ResolveAsync("[]")`. Then `ResolveAsync("[]")` once more | not resolved, the first `[]`: an error that contains "await a decision", and the session file's bytes unchanged. The ONE resolve: the stage is the stage's `AdvancesTo` (`CodeReview` with `PlanProceeded` for the plan, `Done` for the others); on disk `HumanGate`, `AwaitingResolve` and `AdvanceOnResolve` false, `Pending` empty. The resolve after it: an error that contains "no completed round awaiting decisions", and the stage where it was — once, not twice. The decisions are kept: `State.Rejections` holds the rejection made before the release (resolved) or in the one resolve (not resolved), and the held round's row in `coai.db` has `rejected == 1` and the finding's `resolution` and reason as recorded. The held round keeps `call_human` and `LegacyHold.Note`. Plan: `ReviewCodeAsync` is not refused with "plan gate comes first". Feature: `RoundsRunThisStage` is still 2, and a third round (`again: true`) is refused as today | RED: `status` leaves the file held; the resolved rows answer "no completed round awaiting decisions" | `AwaitingResolve = s.AwaitingResolve` in `LegacyHold.Released` (epoch 1's wording) → the four resolved rows are refused; `AdvanceOnResolve = false` → no row advances |
| T57 | `AReleasedCodeHold_ClosesItsEpicOnThatResolve_Once` (Theory: resolved, not resolved) | the code hold in the arrangement of `CadenceGateScenarioTests.APassedCodeRound_ClosesTheEpic_AndStatusSaysSo` (`:364` — the cadence on, the session holding its plan and its epic); stripped; under the same settings with policy auto: `StatusAsync`, then the one resolve, then `StatusAsync` for the plan | after the release and before the resolve, `cadence.epicsClosed` does not contain the epic; after the resolve it contains it exactly once, and the cadence record's verdict for it is `call_human` — the held round's | RED: the stage stays `CodeReview` and the epic is never closed | move `Stage` inside `LegacyHold.Released` (advance at once, the road the parent weighed and did not take) → the session reads `Done` and `epicsClosed` stays empty |
| T58 | `AfterTheOneResolve_ALaterCallHumanRound_DoesNotAdvanceOnAPlainResolve` | T56's plan row, resolved, to its end (stage `CodeReview`). Then under the HUMAN service a code round whose finding gates with the budget spent → `call_human`; `ResolveAsync(<a reasoned reject>)` | the stage is still `CodeReview`, `HumanGate` true, the new notice open: the release left nothing behind for a later hold to advance on | RED: the stage never leaves `PlanReview` | — (the flag is cleared by `RoundMachine.Resolve`, which is not this story's; T21 shows what a release without that resolve leaves, and T56's break-it is this story's line) |

### S1.10.13 — A settings read that failed: `ASettingsReadThatFailedTests` (new), `PanelServiceHostTests` (existing), `SessionPolicyTests`, `AHoldFromBeforeAutoIsReleasedTests` (D14, D15, J10)

The busy and the denied read are driven through the host's `readText` seam (`PanelServiceHostTests.cs:195-203`). The host
of T64 and T65 is a real `PanelServiceHost` over the fixture's data folder with only `COAI_DATA_DIR` in its
environment: both calls are refused before any reviewer is launched, so it needs no reviewer of its own. (If an entry
refuses earlier for want of a reviewer, the file also carries the vendor rows `WriteVendors` writes,
`PanelServiceHostTests.cs:29`; the assertions do not change.)

| # | Test | Setup and action | Expected | First run | Break-it |
|---|---|---|---|---|---|
| T59 | `ASettingsReadThatFailedTests.TryRead_TellsAFileThatGaveNothing_FromOneThatIsNotThere` (Theory) | `SettingsFile.TryRead(path, readText)` over: `{}`; `{"COAI_ON_EXHAUSTED":"human"}`; an empty file; `not json`; `[]`; `null`; a seam that throws `UnauthorizedAccessException`; a plain `IOException`; an `IOException` with HResult `0x80070020`, and `0x80070021`; `FileNotFoundException`; `DirectoryNotFoundException` | `Values` (0 entries); `Values` (1); `Unusable` ×6, each with the reason § S1.5.2 gives; `Busy` ×2; `Missing` ×2. And `SettingsFile.Read(path, readText)` answers an empty dictionary for every row but the second — the one-shot modes read as they did | RED: the six `Unusable` rows read `Values` | answer `Values([])` for a root that is not an object → the `[]` and `null` rows |
| T60 | `PanelServiceHostTests.ABusyReadAtStart_IsReadAgain_AndThePolicyIsTheOperatorsOnceItReads` | the file holds `{"COAI_ON_EXHAUSTED":"human"}`; a host built while the seam is busy; `host.Current.Settings` three times; then the seam free, the file untouched; `host.Current.Settings` | while busy: `Rounds.IsAutonomous`, `PolicyUnread`, the SAME service each time; the note exactly once — one Warning and one notice (`unrecognised-setting`, class `stand-down`, subject the settings path) with the same sentence, which contains the path, "held open by its writer", "Auto by default" and "not released". After: `Rounds.OnExhausted == Human`, not autonomous, `PolicyUnread` false | RED: after the seam is free the policy is still auto — the stamp was recorded before the failed read (F25); and no note | `_stamp = stamp` after a failed first read → the same RED |
| T61 | `PanelServiceHostTests.AnUnusableFile_RunsAutoWithItsNote_IsReadAgainWithoutARebuild_AndIsTheOperatorsOnceRepaired` (Theory: `not json`, empty, `[]`, denied through the seam × at start, on a reload after a good `human` file) | `host.Current` three times; then (a) the file rewritten as `{"COAI_ON_EXHAUSTED":"human"}`; (b) the file deleted; (c) for the denied rows the seam stops throwing, the file untouched | `Rounds.IsAutonomous` and `PolicyUnread`; the SAME service three times; exactly one Warning and one notice for that stamp, the sentence naming the path and its reason. (a) `Human`, `PolicyUnread` false; (b) auto, `PolicyUnread` false — a file that is not there is a read that succeeded; (c) the file's own values | RED: `PolicyUnread` false and no note; (c) keeps the defaults until the file changes | build a service on every call for an unusable file (drop the `_unreadAt` test) → three services and three notes |
| T62 | `PanelServiceHostTests.APolicyNamedInTheEnvironment_IsRead_WhateverTheFileDoes` (Theory: `auto`, `human` in the host's environment) | the file is `not json` | `PolicyUnread` false; the policy is the environment's; the note is still written, and says "comes from this server's own environment" and not "Auto by default" | RED: no note | drop the environment test in `Assemble` → `PolicyUnread` true |
| T63 | `SessionPolicyTests.AFailedRead_ReleasesNothing` (Theory: recorded, not recorded) | a held session from before `auto`; auto settings `with { PolicyUnread = true }`; `Released`, and `CurrentUnclaimed`. Then the same with `PolicyUnread = false` | first: the same object, `NotWritten == LegacyHold.SettingsUnread`; no card touched, the session file's bytes unchanged, no claim taken; one Warning that contains "could not be read" and not "auto released". Then: released | RED: `NotWritten` is empty | drop step 3 of `Released` → the first call releases |
| T64 | `AHoldFromBeforeAutoIsReleasedTests.ABusyReadWithHumanBehindIt_ReleasesNothing_AndTheNextCallRunsHuman` (Theory: recorded, not recorded) | `HeldAsync`, the round resolved, stripped; `settings.json` in the fixture's folder holds `{"COAI_ON_EXHAUSTED":"human"}`; a host built while the seam is busy; `host.Current.ReviewPlanAsync`; the seam free; `host.Current.ReviewPlanAsync` | first: the error is `LegacyHold.NotWrittenSentence(<plan>, LegacyHold.SettingsUnread)`, exactly; the file held with no marker; the notice open; not recorded: no new file under `escalations/`. Second: `GateHeld` — the policy is `human` (not recorded: its notice re-issued, as today); the marker still absent (D8). No line anywhere contains "auto released" | RED: the first call answers `GateHeld` | drop step 3 → the first call releases a hold the operator's `human` keeps |
| T65 | `…AMalformedFile_ReleasesNothing_UntilItIsRepairedOrRemoved` (Theory: repaired as `{}`; removed; repaired as `{"COAI_ON_EXHAUSTED":"human"}`) | the same hold; `settings.json` is `not json`; `host.Current.ReviewPlanAsync` twice; then the row's repair; `host.Current.StatusAsync`, then `ReviewPlanAsync` | twice the `SettingsUnread` sentence, the hold unchanged on disk. Then — `{}` and removed: `Released()`, and the entry answers `LegacyHold.ReleasedSentence`; `human`: `Kept()`, and the entry answers `GateHeld` | RED: `GateHeld` on the first call | as T64 |

**Count:** 67 scenarios (T16 is an existing test turned red and green again; T1–T48 keep their numbers from epoch 1,
T49–T65 are epoch 2's, T66 and T67 — the restore — stand in § S1.10.9 beside T50). **Under `human`, unchanged:** § S1.9, the suites the stories document lists in § 8.2 (*Must
stay green*), and the host's existing tests — `ABusyRead_KeepsTheLastGoodSettings_SaysSoOncePerStamp_AndRecovers`,
`ADeniedSettingsFile_IsNotKeptAsTheLastGood`, `AMalformedFileOnReload_GivesDefaults`,
`AnEmptyFileOnReload_GivesDefaults` — with no assertion changed.

## S1.11 — Commit order and commands

One coder, on `feat/auto-mode-e1`. Each commit builds, and the whole suite is green at commits 2, 3, 4, 5, 6a, 6b and 7.
Commits 6a and 6b are epoch 2's; the other numbers are epoch 1's and are kept, because the scenarios name them.

| # | Commit | Holds |
|---|---|---|
| 1 | `test(session): absent, false and true come back from a build before auto` | T8 only, on the unchanged tree; its break-it seen and restored |
| 2 | `test: name human where the hold suites took the default` | § S1.9 step 1; no production file |
| 3 | `feat(core): the policy pair on disk — a file's constructor keeps absence` | § S1.2 with the code constructor's default STILL `Human`/`false` and `PolicyOf` as it is; `SessionStore.cs:704`; the committed list; T2, T5–T7, T9–T17 |
| 4 | `feat(server): no policy named is policy auto` | the code constructor's two lines, `PolicyOf`, C5; T1, T3, T4; § S1.9 step 2 with its list |
| 5 | `feat(core): a session follows the current policy; a hold from before auto keeps its marker` | `LegacyHold` (§ S1.3, with its note and its two sentences); the stamp at `RoundEngine.cs:279`; T18–T26 |
| 6 | `feat(server): a notice can be withdrawn and put back, and says when it could not` | § S1.4 — `Withdraw` and `Restore`; T27–T29, T66 |
| 6a | `feat(store): one round's note, and nothing else of its row` | § S1.5.1; T52 |
| 6b | `feat(server): a settings read that failed is told apart, read again and noted` | § S1.5.2 — `SettingsRead.Unusable`, `StartLayer`, the host, `PolicyUnread`, `StartupNotices.SettingsUnread`; T59–T62 |
| 7 | `feat(server): a hold from before auto is released by whichever call reads it first` | § S1.5; T30–T46, T49–T51, T53–T58, T63–T65, T67 |
| 8 | `feat(bench): the stored policy is read with its autonomous member` | § S1.6; T47–T48 |
| 9 | `docs(tests): the catalogue names the hold-authority scenarios` | one row in `research/module_tests.md` beside the *A HELD gate is released by a person's answer* row (`:172`): the flow, the classes of § S1.10.5–7 and § S1.10.9–13, the RED symptoms seen, and J8's residuals and "not proved" |

Commands — the stories document's § 3, from the repository root; never `dotnet test`:

```bash
dotnet format dew_flow_connect_other_ais.slnx --verify-no-changes --no-restore
node .agents/conventions/tools/low-priority.mjs dotnet build dew_flow_connect_other_ais.slnx -c Debug -m:4
#   read the exit code: a failed build leaves the previous binaries, and a suite run over them proves nothing
node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests --filter-class "*LegacyHoldTests"
#   and: *PanelConfigPolicyTests  *UnknownSettingValueTests  *ASessionFileKeepsWhatANewerServerWroteTests  *SessionPolicyTests
#        *EscalationsTests  *ASessionFollowsTheCurrentPolicyTests  *AHoldFromBeforeAutoIsReleasedTests
#        *AHoldThisBuildRaisedIsNotReleasedByAutoTests  *RoundNotesTests  *AReleasedHoldAdvancesOnceTests
#        *ASettingsReadThatFailedTests  *PanelServiceHostTests
node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests          # the whole suite
node .agents/conventions/tools/low-priority.mjs ./src_bench/CoaiBench.Tests/bin/Debug/net10.0/CoaiBench.Tests
# after commit 9 — the configuration CI runs:
node .agents/conventions/tools/low-priority.mjs dotnet build dew_flow_connect_other_ais.slnx -c Release -m:4
node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Release/net10.0/CoaiMcp.Tests
node .agents/conventions/tools/low-priority.mjs ./src_bench/CoaiBench.Tests/bin/Release/net10.0/CoaiBench.Tests
node .agents/conventions/tools/plan-lifecycle.mjs .
```

The coder's summary gives, per scenario: the RED message as the runner printed it (or "GUARD"), the GREEN line, and the
break-it's message. For T8: the sha of commit 1 and both observations there.

## S1.12 — What the Opus reviewer checks

1. `PanelConfig`: the file constructor's `autonomous` has no default; `Autonomous` has no initializer; both setters are
   private; `[JsonConstructor]` is on the file constructor and nowhere else.
2. `git grep -n "Autonomous = " -- src_mcp/core src_mcp/src` shows the marker written in `SessionState.cs` only (the two
   constructors and `UnderPolicyOf`); `PanelSettings.Autonomous` (`COAI_AUTONOMOUS`) is another member and is not touched.
3. `git grep -n "autonomous: null" -- src_mcp/src src_mcp/core` shows `SessionStore.cs` only.
4. `LegacyHold.Stands` is the single eligibility test: no other file compares `Autonomous` or `PredatesAuto` with a hold.
5. In `RoundEngine.RunStageAsync` the order is: claim, load, `Released`, the not-written refusal,
   `ApplyAnyHumanDecision`, the Proceed check, the released-and-not-resolved refusal, `Stamped` with the `SecurityLane`
   refresh, `stage.Begin`. Nothing is saved for a refusal, and nothing is re-issued under `auto` for a hold from
   before `auto`.
6. `SessionPolicy.Released`, in the order of § S1.5: the answer on file is asked before anything is withdrawn and again
   after the withdrawal; `PolicyUnread` is read before the first `Withdraw`; every notice is withdrawn before the
   save; each of the four failures returns the session it was given with its own reason; the note is in the same save
   as the release; the projection comes after the save and cannot undo it. No line carries a question's or an
   answer's text.
7. `CurrentUnclaimed` takes the claim only when there is a release to write, reads again under it, and disposes it.
8. `Withdraw` releases the turn before it writes and does not rewrite an expired card. `Expire` is unchanged.
9. The five entries of § S1.5 and no sixth; `open` unchanged; `HeldSentence`, `WithTheHoldReissued` unchanged.
10. Commit 2 changes no assertion and no production file; commit 4's list of pinned tests is in the PR, each with the
    failure that showed it relied on the default.
11. T8 was seen at commit 1, with its break-it. T10 and T21 assert the refuted outcome and say so in their names.
12. `ClaimRefusal`, `GateHeld`, `Unresolved`, `StillOpen`, every verdict, every `ask_human` reply and every tool text
    are byte for byte as before. The two new sentences are `LegacyHold`'s and are the parent's § 4.3 word for word.
13. New comments and log lines say "policy auto", never "autonomous" alone (C1).
14. No file that was under 800 lines passed it; `RoundEngine.cs` grew by at most twelve lines, `RoundsDb.cs` by none.
15. `LegacyHold.Released` writes `AwaitingResolve = true` and `AdvanceOnResolve = true` (the parent's § 4.3, entry
    1.2), and nothing in this story moves `Stage`: the only advance is `RoundMachine.Resolve` through
    `PanelService.Finish`.
16. `git grep -n "RecordRound(" -- src_mcp/src/Server/Rounds/SessionPolicy.cs` prints nothing: the note reaches
    `coai.db` through `RoundNotes.Write` alone, which names one column (the parent's § 4.3, as repaired for U2).
17. `SessionPolicy.AwaitsItsResolve` reads the note by equality with `LegacyHold.Note`; no new member is written to
    the session file.
18. The host: a read that was busy or unusable never becomes `_stamp`; an unusable file builds one service per stamp;
    `PolicyUnread` is set in `Assemble` and nowhere else, and is false when the process environment names the
    policy; a reload that finds the file busy marks nothing. `Program.cs` is unchanged.
19. The note for a failed read is ONE call that writes the Warning and the notice, on the first build too, once per
    stamp, with an existing notice code.
20. T50 writes its answer between the withdrawal and the second read — through the `Writer` seam, not before the
    call — and its `discuss` row asserts the hold stays with its notice open again.
21. `Escalations.Restore` writes `Status` and `ExpiredUtc` and no other member, and touches no answer file. It has two
    callers, both in `SessionPolicy.Released`: step 5, for the ids step 4 answered `Withdrawn` for, and step 2, for
    the hold's notices only. `OfferHeldNotices` and `WithTheHoldReissued` are unchanged; a failed restore is a
    Warning and never a `NotWritten` reason.

## Definition of Done — E1.S1

- [x] U1 settled: the parent's entry 1.2 and its revised § 4.3 (2026-11-16).
- [x] U2 and U3 settled in the parent (2026-11-16): a one-column update of `rounds.note`; the withdrawn cards restored
      when the person's answer is found at the second read.
- [ ] § S1.2–S1.6 built as written; C1–C5 of the stories document hold as edited (X1–X10).
- [ ] T1–T67: each RED seen for the stated reason, each GREEN, each break-it seen and restored — all three in the summary.
- [ ] A hold from before `auto` is released only with no answer of the person's on file and only on a policy that was
      read; its release is noted on the held round, in the file and in `coai.db`; one `resolve` then advances the
      stage once, in each of the four stages (parent § 13, item 4).
- [ ] § S1.9: the two lists in the PR; no assertion of an existing test changed.
- [ ] Debug and Release: the whole `CoaiMcp.Tests` and `CoaiBench.Tests` green; `dotnet format` clean.
- [ ] `research/module_tests.md` names the scenarios and what they do not prove; `plan-lifecycle.mjs` clean.
- [ ] No verdict, no `ask_human` reply, no tool text, no `--features` entry, nothing under `src_vs_code` changed.
