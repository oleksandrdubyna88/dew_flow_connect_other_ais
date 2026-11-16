# PLAN — E1.S1 spec: the stored policy and the hold's authority — `autonomous` on disk, and the one hold `auto` may release (CORNERSTONE)

> Status: **spec only, 2026-11-16, by Fable, written before its code — nothing coded.** One point waits for the
> coordinator's word before commit 5 is coded: U1 (§ S1.8).
> Cornerstone spec of story E1.S1 of [PLAN_auto_never_asks_a_person_E1.md](PLAN_auto_never_asks_a_person_E1.md) (its § 7;
> parent [PLAN_auto_never_asks_a_person.md](PLAN_auto_never_asks_a_person.md), § 3 requirements 4, 8 and 9, § 4.1, § 4.3,
> § 6, § 8 steps 2, 3, 4 and 6, note N2). It holds the security judgement on who may release a hold (§ S1.7), which must
> stand before any S1 code. It keeps no change ledger: a change is an entry in the parent. It is promoted with the E1
> document.
>
> Scope: `src_mcp` (core, server, tests) and `src_bench`. NEW `src_mcp/core/Rounds/LegacyHold.cs`,
> `src_mcp/src/Server/Rounds/SessionPolicy.cs`; changed `SessionState.cs` (`PanelConfig`), `PanelSettings.cs`
> (`PolicyOf`), `SessionStore.cs:704`, `Escalations.cs` (`Withdraw`), five call sites, `SettingsApplied.cs`, tests,
> `research/module_tests.md`. No verdict, no `ask_human` reply, no tool text, no `--features` entry, nothing under
> `src_vs_code`. Coder: Sonnet. Review: Opus. Read at `1f0ae332` (code identical to `origin/main` 4cb32a76). Paths are
> from the repository root.

## Changes to § 4 of the stories document (made in the same working tree)

| # | Contract | § 4 said | This spec says | Why | Who must follow |
|---|---|---|---|---|---|
| X1 | C1 | `bool? Autonomous { get; init; }` | `Autonomous` and `OnExhausted` have a `private init`. Two constructors (code, file). New read-only `PredatesAuto`, and `UnderPolicyOf(current)` — the only way to change the pair | The pair cannot be written half, and the marker cannot be made absent, outside the type (§ S1.2) | S2 and S3 still read only `IsAutonomous` |
| X2 | C2 | an auto config in a test is `Uniform(max, threshold, GoodEnough) with { Autonomous = true }` | it is `PanelConfig.Uniform(max, threshold)` — no policy named is policy auto. The `with` form does not compile | X1 | S2 § 8.2 helper `Auto`, S3 § 9.2 row 6 — both edited |
| X3 | C4, the name | "the parent's `CurrentPolicy`" | `SessionPolicy.Current(session)` for a caller that holds the claim; `SessionPolicy.CurrentUnclaimed(session, repoPath, at)` for one that does not; the begin calls the two halves (`Released`, then `LegacyHold.Stamped` at `:279`) | § S1.5 | S3 calls `CurrentUnclaimed`, which S1 already wired at `AskHumanService.cs:100` |
| X4 | C4, `HeldSentence` | `HeldSentence` passes the session through it | its one caller does (`PanelService.WhyNotAFreshReview`, `:1152-1158`), before the `switch` | `HeldSentence` is only reached for a held session and returns a sentence; after a release there is none | — |
| X5 | C4, F0 and F1 (plan round on the stories document, accepted by the coordinator) | the marker is stamped on every deciding load; the notice is withdrawn | while a hold from before `auto` stands its config is not stamped; the notices are withdrawn first and the session saved second; a notice that could not be withdrawn keeps the hold | § S1.1 D8, D9 | — |
| X6 | C4, its last sentence; S3 § 9.2 | `autonomous = session?.State.Config.IsAutonomous ?? _settings.Rounds.IsAutonomous` | `autonomous = _settings.Rounds.IsAutonomous` | Under X5 a held session can have `IsAutonomous == false` while the settings say `auto` (a release that could not be written). Read from the session, `ask_human` would write a card and wait | S3 — the line is edited |

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

D1–D7 answer the seven inputs of the stories document's § 7. D8 and D9 are F0 and F1 of the plan round.

| # | Input | Decision |
|---|---|---|
| D1 | The marker is absence | `Autonomous` is `null` only when a session file had no `autonomous` member. A file is read through a constructor of its own whose `autonomous` parameter has NO default (M3). Code builds a config through another constructor, which always writes `true` or `false`. The null-config path (`SessionStore.cs:704`) calls the file constructor with `autonomous: null` and `Human`: a file with no config is a file from before `auto`. No initializer, no positional default (M1, M2) |
| D2 | A whole file through an older build | The enum stays `GoodEnough`; no new name is written. The tree at `1f0ae332` IS a build before `auto` that has the bag, so T8 is written and seen GREEN there first (commit 1), with its break-it. After the member lands, the same test, unchanged, pins this build's own load and save. A released older binary is not run (§ S1.7 J8) |
| D3 | Which loads take the claim | The begin and `resolve` release under the claim they hold. `status`, `ask_human` and the fresh-document check take the session's claim for the release only, once, and read the session again under it. Busy → nothing is written, the call answers the session as read; the release is done by the call that holds the claim, or by the next call. No call is refused because of this |
| D4 | The hold with no recorded question | `Released` runs before the re-issue branch (`RoundEngine.cs:100`), so under `auto` such a hold is released and nothing is re-issued. Under any other policy it is re-issued as today, and its config stays without the marker (D8). A hold this build raised is untouched |
| D5 | The defaults and the existing tests | Two steps. Before the flip, one commit names `human` in every test file that speaks of a hold (a list made by one `grep`), with defaults unchanged. Then the flip; a test it reddens gets `human` named and is listed. The other tests run under the shipped default (§ S1.9) |
| D6 | The withdrawal's reason | In the Warning line only. An expired card is not drawn (F17), so no member would show it; the card leaves the active list |
| D7 | `CurrentPolicy`'s home | The decisions are pure and live in core: `LegacyHold` and `PanelConfig.UnderPolicyOf`. The reads and writes are `SessionPolicy` (`src_mcp/src/Server/Rounds/SessionPolicy.cs`), built once in `PanelService` and handed to the engine and to `AskHumanService` |
| D8 | F0 — provenance lost by a stamp under `human` | `LegacyHold.Stamped` leaves the config of a session exactly as loaded while a hold from before `auto` stands. The marker is written only when no such hold stands. The begin stamps AFTER the person's decision is applied (`:279`, after `:267`), so a hold raised after a person released the old one is this build's (T45) |
| D9 | F1 — the window between the release and the withdrawal | `Escalations.Withdraw` answers `Withdrawn`, `NotThere` or `Failed`. Every notice is withdrawn first; any `Failed` → nothing is saved, the hold and its `HoldQuestions` stay, a Warning, and the next deciding load tries again. Then the released session is saved; a save that fails leaves the hold too. `Withdraw` does not rewrite an expired card, so a second pass changes nothing |

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

    /// <summary>The session as an auto verdict would have left it: nothing held, the stage advances on resolve.</summary>
    public static SessionState Released(SessionState s, PanelConfig current) => s with
    {
        HumanGate = false,
        HoldQuestions = [],
        AwaitingResolve = true,          // U1, § S1.8
        AdvanceOnResolve = true,
        Config = s.Config.UnderPolicyOf(current),
    };

    /// <summary>The config a deciding load reads: the settings' policy — but untouched while a hold from before auto stands.</summary>
    public static PanelConfig Stamped(SessionState s, PanelConfig current) =>
        Stands(s) ? s.Config : s.Config.UnderPolicyOf(current);
}
```

`Stamped` returns a config, so it cannot touch `EscalationsUsed`, `RoundsRunThisStage` or the pending decisions (parent
§ 4.1). `Released` stamps in the same step, so a released session is saved once and never keeps an absent marker.

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

## S1.5 — `SessionPolicy` and the five entries (`src_mcp/src/Server/Rounds/SessionPolicy.cs`, new)

```csharp
internal sealed class SessionPolicy(PanelSettings settings, SessionStore store, Escalations escalations, Serilog.ILogger log)
{
    /// <summary>The config under the current settings — <see cref="LegacyHold.Stamped"/>, on a session.</summary>
    internal PersistedSession Stamped(PersistedSession session);

    /// <summary>Releases a hold from before auto. The CALLER HOLDS the session's claim.</summary>
    internal PersistedSession Released(PersistedSession session);

    /// <summary><see cref="Released"/>, then <see cref="Stamped"/> — for a caller that holds the claim.</summary>
    internal PersistedSession Current(PersistedSession session);

    /// <summary>The same for a caller that holds NO claim: it takes the claim for the release alone.</summary>
    internal PersistedSession? CurrentUnclaimed(PersistedSession? session, string repoPath, SessionAddress at);
}
```

`Released`, step by step:

1. `!settings.Rounds.IsAutonomous || !LegacyHold.Stands(session.State)` → return `session` (the same object). This is the
   whole authority check, and it reads the config AS LOADED.
2. `Withdraw(id, now)` for EVERY id of `HoldQuestions` (no short circuit).
3. Any `Failed` → `log.Warning("policy auto could not withdraw notice(s) {Ids} of the {Stage} hold of session {SessionId} — the hold stays, and the next call tries again", …)`; return `session`.
4. `released = session with { State = LegacyHold.Released(session.State, settings.Rounds) }`; `store.Save(released)`.
   `SessionStoreException` → `log.Warning(e, "policy auto could not save the release of the {Stage} hold of session {SessionId} — the hold stays, and the next call tries again", …)`; return `session`.
5. `log.Warning("policy auto released the {Stage} hold of session {SessionId} raised under {Policy} — a hold from a build before auto; {Withdrawn} notice(s) withdrawn", stage, id, <OnExhausted as loaded>, <count of Withdrawn>)`; return `released`.

`CurrentUnclaimed`: `null` → `null`. Step 1's test false → `Stamped(session)`, no claim taken, nothing written. Else
`SessionClaim.TryTake(settings.DataDir, repoPath, at.Branch, at.Document, at.Feature)`; `null` → an Information line, return
`session`. Else load the session AGAIN under the claim and return `Current(fresh)` (or `session` when it cannot be read).

| Entry | Where | The call | Claim |
|---|---|---|---|
| begin — `review_plan`, `review_code`, `review_document`, `review_feature` | `RoundEngine.cs:267` | `session = ApplyAnyHumanDecision(_policy.Released(session));` | the round's own (`:252`) |
| begin, the stamp | `RoundEngine.cs:279` | `{ Config = LegacyHold.Stamped(session.State, _settings.Rounds) with { SecurityLane = _settings.SecurityLane.Gate } }` | the same |
| `resolve` | `PanelService.cs:1656-1662`, after the null check, before `OfferHeldNotices` | `session = _policy.Current(session);` | its own (`:1647`) |
| `status` | `PanelService.cs:567` | `var session = _policy.CurrentUnclaimed(_store.Load(…), repoPath, at);` | taken for the release |
| `ask_human` | `AskHumanService.cs:100`, before `_desk.Facts` | the same | taken for the release |
| a fresh document review (`HeldSentence`'s caller) | `PanelService.cs:1152-1158` | `CurrentUnclaimed` on the loaded document session, `at = new SessionAddress(branch, identity, string.Empty)`, then today's `switch` | taken for the release |

`SessionPolicy` is built in `PanelService` after `_escalations` (`:102`) and handed to `AskHumanService` (`:116`) and
`RoundEngine` (`:144`). `RoundEngine.cs` grows by two lines (the parameter and its field) and changes two. `HeldSentence`,
`WithTheHoldReissued` and `ApplyAnyHumanDecision` keep their bodies. `open` is not an entry: it reads no hold and keeps
the config it loaded.

What a caller sees after a release: the session awaits a `resolve` and advances on it. So a begin answers
`RoundMachine.Unresolved` (never `GateHeld`), `resolve` with no decisions advances the stage, `status` says
`awaitingResolve: true`, and the fresh-document check answers `DocumentSessions.StillOpen`.

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
that release (O2).

| # | Judgement | Pinned by |
|---|---|---|
| J1 | **The caller's argument releases nothing, under every policy.** `ClaimRefusal` is not changed. `resolve(humanDecision: "proceed")` on a hold this build raised is refused with `ClaimOnAHeldGate` while the settings say `auto` | T41 |
| J2 | **One predicate, read before any stamp.** `LegacyHold.Stands` is the whole test of which hold is eligible, `SessionPolicy.Released` is its only caller that writes, and every entry calls `Released` on the session as loaded. `LegacyHold.Stamped` never writes the marker onto a standing eligible hold, so reading a hold can never change what it is | T18, T22, T32, T44 |
| J3 | **A hold this build raises always carries the marker.** A hold is raised only by a round (F4). A round runs only after a begin that was not refused, a begin refuses every held session (F5), and a session that is not held is stamped before `stage.Begin` (`:279`). So no round ever runs on a config whose marker is absent, and the save that records the hold (F9) writes `autonomous` | T23, T26, T45 |
| J4 | **A later `auto` never releases it.** Its config has `false` or `true`; `Stands` is false; no setting is read after that. However the policy became `auto` — the word written, the key deleted, an empty value, an unreadable settings file (F19), a client `env` — the same predicate decides | T39, T40, T43 |
| J5 | **Code cannot make the marker absent.** `Autonomous` has a `private init`; the code constructor writes `true` or `false`; `UnderPolicyOf` writes a `bool`. The file constructor is the one way, and production calls it at `SessionStore.cs:704` only | T5, T6, T15; checklist item 3 |
| J6 | **The release is all or nothing, and repeatable.** Notices first, the session second (D9). A crash or a failed write between them leaves a held session whose config still has no marker; the next deciding load releases it again and rewrites no card | T30, T31 |
| J7 | **The release is written under the session's claim**, and an entry that takes the claim reads the session again under it — a release never overwrites what a `resolve` saved in between | T33 |

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
exists today and which [PLAN_a_signed_answer_releases_the_gate.md](PLAN_a_signed_answer_releases_the_gate.md) names. **Not
proved by these tests:** that a RELEASED older `coai-mcp` keeps the member — the evidence is this repository's tree at
commit 1 and the bag's own suite; and that an answer was written by a person.

## S1.8 — What S1 does not close, and what is reported upstream

- **U1 — reported, waits for the coordinator's word (commit 5).** The parent (§ 4.3) and C4 say the release keeps
  `AwaitingResolve`. A hold whose round the caller already resolved has `AwaitingResolve == false` (F6) — the ordinary
  case, because `resolve` is what a caller is told to do. Kept `false` with `AdvanceOnResolve = true`, the flag has no
  resolve to consume it and survives into a later round: a `call_human` or `escalated` round under a later non-auto
  policy then advances on a plain `resolve` (F6; T21 shows it). Without the flag, a feature review held at its cap can
  never close: `resolve` has nothing to resolve and a third round is refused (`RoundMachine.cs:314`). The spec's reading:
  the release sets `AwaitingResolve = true` — equal to "kept" whenever a round awaits, and the state every auto verdict
  leaves. § S1.3, T19–T21 and the last paragraph of § S1.5 are written for it. If the word is another one, only
  `LegacyHold.Released` and those scenarios change.
- **A release that could not be written.** Busy claim, failed withdrawal, failed save: the session stays held under
  `auto` until the next deciding call. A begin then answers `GateHeld`, whose words ("ask a person") are not true for
  that moment; the Warning says why. Handed to S3: N3's sentence ("held by this server under a policy that asks a
  person") is inexact for this case — `LegacyHold.Stands(session.State)` tells the two apart.
- **A card no session records.** A notice an older build wrote without recording it on the session is not withdrawn
  (parent § 4.3 withdraws recorded questions). The retention deletes it seven days after it was asked (F18).
- **An unreadable settings file now means `auto`** (F19): it read as `Human` by default before. Holds are not affected
  (J4); future rounds are. The parent's § 6 does not name this cause. Reported, not changed here.
- **The closing verdict of an epic.** A released code-stage hold advances on `resolve`, and `_cadence.Close`
  (`PanelService.cs:1792`) records the last round's verdict, `call_human`. S2 corrects `CadenceState`'s wording.
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
their input, `Withdraw` returning `Failed`, `SessionPolicy` whose methods return their input. **RED** is the failure seen
with those stubs (or, where a commit is named, at that commit). **GUARD** is a test that is green before the code because
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
| T20 | `AReleasedHold_ThenAnEmptyResolve_AdvancesTheStage` (Theory: plan, code, document, a feature session with two rounds run × resolved or not) | `Released`, then the stage's begin, then `RoundMachine.Resolve(state, [])` | the begin is `Refused(Unresolved)`; the resolve is `Moved` to the stage's `AdvancesTo` (`PlanProceeded` for a plan), `AdvanceOnResolve` false after it | RED: the resolve does not advance, and a resolved hold's begin is `Refused(GateHeld)` | `AdvanceOnResolve = false` in `Released` → no advance |
| T21 | `TheNaiveRelease_ThatKeepsAwaitingResolveFalse_WouldLetALaterCallHumanRoundAdvance_AndThatIsThePoint` | by hand: a resolved held session `with { HumanGate = false, HoldQuestions = [], AdvanceOnResolve = true }`, config `UnderPolicyOf(Mine(Human))`; `CompleteRound(s, GateResult.Empty, Nobody)`; `Resolve(state, [])` | the verdict is `CallHuman` and the resolve MOVES to `CodeReview` with `HumanGate` still true — asserted, as the defect of U1 | GUARD | — (a refuted shape) |
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
| T30 | `SessionPolicyTests.TheNoticesAreWithdrawnBeforeTheSessionIsSaved_AndAFailedSaveIsRetried` (F1) | a held session from before `auto` with notice `n-1`, planted with a working store; a `SessionStore` whose `FileWriter` throws `IOException`; auto settings; `Released(session)`. Then a policy over a working store: `Released(store.Load(…)!)` | first: the returned session still held with `["n-1"]`; on disk still held; `n-1` expired at `t1`; one Warning "could not save the release". Second: on disk `HumanGate` false; `n-1` still expired with `ExpiredUtc == t1`; one Warning that contains "auto released the" | RED: nothing is released | save before the withdrawal → after the first call `n-1` is still open |
| T31 | `…ANoticeThatCannotBeWithdrawn_KeepsTheHold_AndTheNextLoadReleasesIt` (F1, the cadence consultation) | notices `n-1` and `q-2`, both open; an `Escalations` whose `Writer` throws for `n-1` only; `Released`. Then working | first: the SAME session object; on disk held with `["n-1", "q-2"]`; `n-1` open, `q-2` expired; a Warning that names `n-1`; no "auto released". Second: released; `n-1` expired; `q-2`'s `ExpiredUtc` unchanged | RED | treat `Failed` as done → the first call releases with `n-1` open |
| T32 | `…Released_WritesNothing_ForAnyOtherSession` | (before-auto hold, `Human` settings); (this build's hold — `Uniform(1, 0, Human)` — auto settings); (before-auto file, not held, auto) | the same object each time; the session file's bytes and every card unchanged; no Warning | GUARD | `Stands => s.HumanGate` → row 2 is released |
| T33 | `…CurrentUnclaimed_ReleasesUnderTheSessionsClaim_AndReadsAgainUnderIt` | (a) the test holds `SessionClaim.TryTake(…)`; `CurrentUnclaimed`. (b) the claim free. (c) a STALE held copy passed in while the file on disk is already released | (a) a held session back, nothing written; (b) released, and `SessionClaim.TryTake` succeeds afterwards; (c) the disk's session back, nothing written, no "auto released" | RED (b) | remove the `TryTake` → (a) writes; remove the second load → (c) saves the stale copy |

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
  that contains "auto released the" and the session id.

| # | Test (each a Theory over `recorded`) | Action after `HeldAsync`, `StripTheMarker`, under `AutoService` | Expected beside `Released()` | First run | Break-it |
|---|---|---|---|---|---|
| T34 | `AReviewEntry_ComesFirst` | `resolve(RejectIt)` under `human` BEFORE the strip (the hold stays, the round is resolved); then `ReviewPlanAsync` | the error is `RoundMachine.Unresolved`, and contains neither "call_human" nor "a person has to decide"; then `ResolveAsync("[]")` answers stage `CodeReview` | RED: the error is `GateHeld`; the file is held | `RoundEngine.cs:267` back to `ApplyAnyHumanDecision(session)`; and, for D4, `Released` called AFTER `ApplyAnyHumanDecision` → the not-recorded row finds a re-issued notice |
| T35 | `Resolve_ComesFirst` | `ResolveAsync(RejectIt)` | stage `CodeReview`, `PlanProceeded` | RED: stage `PlanReview`, the file held | drop `_policy.Current` in `ResolveUnderClaim` |
| T36 | `Status_ComesFirst` | `StatusAsync` | `awaitingResolve: true`; no error | RED: the file held | drop the call at `PanelService.cs:567` |
| T37 | `AskHuman_ComesFirst` | `AskHumanAsync(_repo, Branch, "May I ship?")` | it returns; the reply's `status` is not `no_answer_yet`; no new file under `escalations/` | RED: a second question file, `no_answer_yet` after 300 ms, the file held | drop the call at `AskHumanService.cs:100` |
| T38 | `AFreshDocumentReview_ComesFirst` | the arrangement of `ADocumentIsReviewedEndToEndTests.ANewReviewOfAHeldDocument_IsRefused_AndTheHoldStays` (`:638`), the document session stripped; then `review_document` with `newReview: true` | the reply is `DocumentSessions.StillOpen`, not `GateHeld`; after `resolve` of the document a fresh review starts | RED: `GateHeld` | drop the call in `WhyNotAFreshReview` |

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

**Count:** 48 scenarios (T16 is an existing test turned red and green again). **Under `human`, unchanged:** § S1.9, and
the suites the stories document lists in § 8.2 (*Must stay green*).

## S1.11 — Commit order and commands

One coder, on `feat/auto-mode-e1`. Each commit builds, and the whole suite is green at commits 2, 3, 4, 5 and 7.

| # | Commit | Holds |
|---|---|---|
| 1 | `test(session): absent, false and true come back from a build before auto` | T8 only, on the unchanged tree; its break-it seen and restored |
| 2 | `test: name human where the hold suites took the default` | § S1.9 step 1; no production file |
| 3 | `feat(core): the policy pair on disk — a file's constructor keeps absence` | § S1.2 with the code constructor's default STILL `Human`/`false` and `PolicyOf` as it is; `SessionStore.cs:704`; the committed list; T2, T5–T7, T9–T17 |
| 4 | `feat(server): no policy named is policy auto` | the code constructor's two lines, `PolicyOf`, C5; T1, T3, T4; § S1.9 step 2 with its list |
| 5 | `feat(core): a session follows the current policy; a hold from before auto keeps its marker` | `LegacyHold`; the stamp at `RoundEngine.cs:279`; T18–T26. **Not before U1 is settled** |
| 6 | `feat(server): a notice can be withdrawn, and says when it could not` | § S1.4; T27–T29 |
| 7 | `feat(server): a hold from before auto is released by whichever call reads it first` | § S1.5; T30–T46 |
| 8 | `feat(bench): the stored policy is read with its autonomous member` | § S1.6; T47–T48 |
| 9 | `docs(tests): the catalogue names the hold-authority scenarios` | one row in `research/module_tests.md` beside the *A HELD gate is released by a person's answer* row (`:172`): the flow, the classes of § S1.10.5–7, the RED symptoms seen, and J8's "not proved" |

Commands — the stories document's § 3, from the repository root; never `dotnet test`:

```bash
dotnet format dew_flow_connect_other_ais.slnx --verify-no-changes --no-restore
node .agents/conventions/tools/low-priority.mjs dotnet build dew_flow_connect_other_ais.slnx -c Debug -m:4
#   read the exit code: a failed build leaves the previous binaries, and a suite run over them proves nothing
node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests --filter-class "*LegacyHoldTests"
#   and: *PanelConfigPolicyTests  *UnknownSettingValueTests  *ASessionFileKeepsWhatANewerServerWroteTests  *SessionPolicyTests
#        *EscalationsTests  *ASessionFollowsTheCurrentPolicyTests  *AHoldFromBeforeAutoIsReleasedTests
#        *AHoldThisBuildRaisedIsNotReleasedByAutoTests
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
5. In `RoundEngine.RunStageAsync` the order is: claim, load, `Released`, `ApplyAnyHumanDecision`, the Proceed check,
   `Stamped` with the `SecurityLane` refresh, `stage.Begin`. Nothing is saved for a refusal.
6. `SessionPolicy.Released`: every notice withdrawn before the save; `Failed` returns the session it was given; the save's
   failure is caught and returns it too; three Warning lines, none with a question's text.
7. `CurrentUnclaimed` takes the claim only when there is a release to write, reads again under it, and disposes it.
8. `Withdraw` releases the turn before it writes and does not rewrite an expired card. `Expire` is unchanged.
9. The five entries of § S1.5 and no sixth; `open` unchanged; `HeldSentence`, `WithTheHoldReissued` unchanged.
10. Commit 2 changes no assertion and no production file; commit 4's list of pinned tests is in the PR, each with the
    failure that showed it relied on the default.
11. T8 was seen at commit 1, with its break-it. T10 and T21 assert the refuted outcome and say so in their names.
12. `ClaimRefusal`, `GateHeld`, every verdict, every `ask_human` reply and every tool text are byte for byte as before.
13. New comments and log lines say "policy auto", never "autonomous" alone (C1).
14. No file passed 800 lines but the two that were over; `RoundEngine.cs` grew by two lines.
15. U1 is settled in the parent or in C4, and `LegacyHold.Released` matches the word given.

## Definition of Done — E1.S1

- [ ] U1 settled before commit 5.
- [ ] § S1.2–S1.6 built as written; C1–C5 of the stories document hold as edited (X1–X6).
- [ ] T1–T48: each RED seen for the stated reason, each GREEN, each break-it seen and restored — all three in the summary.
- [ ] § S1.9: the two lists in the PR; no assertion of an existing test changed.
- [ ] Debug and Release: the whole `CoaiMcp.Tests` and `CoaiBench.Tests` green; `dotnet format` clean.
- [ ] `research/module_tests.md` names the scenarios and what they do not prove; `plan-lifecycle.mjs` clean.
- [ ] No verdict, no `ask_human` reply, no tool text, no `--features` entry, nothing under `src_vs_code` changed.
