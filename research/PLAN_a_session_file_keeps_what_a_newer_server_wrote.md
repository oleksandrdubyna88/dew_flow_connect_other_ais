# PLAN — A session file keeps what a newer server wrote

> Status: **IMPLEMENTED, 2026-10-19** (PR #868, branch `docs/plan-session-keeps-unknown-fields`; not merged or released
> at promotion). Plan gate `good_enough` 2026-10-18 (3 of 4 reviewers; 6 of 8 findings applied, 2 rejected). Code gate
> `good_enough` 2026-10-19 (12 of 16 reviewers; kimi's four failed, not signed in): 30 findings, 5 accepted, 25 rejected
> with reasons. Stories S1–S4 shipped as planned, with these **deviations**:
> 1. The scrub and `RetiredMembers` are their own file, `src_mcp/src/Server/SessionScrub.cs`, not private members of
>    `SessionStore` (which stood at 733 lines). The test seam is `internal RetiredMembers Retired { get; init; }` on
>    `SessionStore`.
> 2. Measured: `JsonTypeInfo.Properties` lists the bag itself (`unknown`) and the `[JsonIgnore]`d `PanelConfig.Catalog`.
>    The scrub and T10 filter `IsExtensionData`, and `catalog` stays a known name, so a catalog an older build wrote is
>    dropped on save (extra test `ACatalogAnOlderBuildWroteIntoTheConfig_IsNotCarriedForEver`).
> 3. The break-it "remove a member from `RoundRecord`" was done as a rename with `[JsonPropertyName]`, which is the form
>    that compiles. T10 named both the gone and the new name.
> 4. T1 also plants a member in `state.config.securityLane` (seven members, not six). Two tests were added beyond T1–T10:
>    the catalog test above and `TheMemberListCheckStillSeesTheTypes`, the companion that keeps T10 from passing on two
>    empty lists.
> 5. The AOT run planted its file in a session that the published binary had written itself on a first `open`; a second
>    process then loaded and saved it. 7 of 7 members were kept, before S2 and again on the final build.
> 6. **The judge's review found a regression the plan did not foresee:** a session file written before the security lane
>    existed (2026-10-01) has no `securityLane`, the source generator gives `null`, and the first scrub threw
>    `NullReferenceException` on save, so `open` would have failed on every such file. `SessionScrub.GateOf` passes a
>    null gate through (RED/GREEN: `AConfigWrittenBeforeAMemberExisted_StillSaves_AndKeepsItsUnknowns`).
> 7. From the code gate: each contract's names are worked out once per type (a `FrozenSet` per `JsonTypeInfo`, bounded
>    by the six types), and a bag is copied only when a name has to go (`ATypesContractNames_AreWorkedOutOncePerType`,
>    `ABagWithNothingToDrop_IsWrittenFromTheDictionaryItWasGiven`).
> 8. D8's release note: checked `release-please-config.json` (`$note`, `$order`). A feature PR carries no
>    `src_vs_code/CHANGELOG.md` line, because the prose for a version is written in that version's release pull request.
>    So the sentence is in PR #868's body for the releaser, and it must go into the next `## Server <version>` section.
>
> **Open tail:** D7 (`Finding`, `CallerDeclaration`, `RoundSize`, `PlanSource` keep no bag) and §9 (six other stores
> with the same exposure), both as planned. One more from the code gate: an absent `securityLane` still LOADS as `null`
> rather than the default gate. The scrub now tolerates it, but normalising it where it is read changes what an old
> file loads as (D4), so it needs its own plan. Not checked: a `Release` AOT publish after deviations 6 and 7 (CI builds it).
>
> Scope: `src_mcp` only — the session records in
> `src_mcp/src/Server/SessionStore.cs` and `src_mcp/core/Rounds/SessionState.cs`, the save in `SessionStore`, the
> `open`/`status` answer in `src_mcp/src/Server/PanelService.cs`, their tests, and `research/module_server.md`. No
> schema step, no extension change, no wire change. All references are to `main` at `2b302c65`.
>
> Related docs: [module_server.md](module_server.md) (§ *Persistence*, `:2014-2020`), the round-authors
> epic E5 [PLAN_a_round_records_who_wrote_the_code_and_how_much_E5.md](PLAN_a_round_records_who_wrote_the_code_and_how_much_E5.md)
> (§3 *An older server strips the snapshot*, §7 item 18 and §10 item 1 — on branch `feat/round-authors-e5` at
> `c1216b42`; see §5 below for the boundary). The owner approved this work on 2026-10-18.

## 1. Symptom

Several `coai-mcp` processes use one data directory: one server per VS Code window
(`src_mcp/src/Server/SessionStore.cs:486-487`), and a window that was not reloaded after an update runs the OLDER
binary. Each of them loads a session file, changes it, and saves it again: `open` does it on every call
(`src_mcp/src/Server/PanelService.cs:497-512`), and so do the rounds (`src_mcp/src/Server/Rounds/RoundEngine.cs:108`,
`:131`, `:592`, `:722`; `src_mcp/src/Server/LiveRound.cs:257`; `src_mcp/src/Server/Rounds/RoundSkips.cs:76`),
`resolve` (`src_mcp/src/Server/PanelService.cs:1741`), `ask_human` (`src_mcp/src/Server/AskHumanService.cs:365`) and
the startup orphan sweep (`src_mcp/src/Server/SessionStore.cs:608-635`).

**An older server writes only the members it knows.** It reads the file with the source-generated
`ServerJsonContext.Default.PersistedSession` (`SessionStore.cs:472`, `:662`) and writes it with the same type info
(`SessionStore.cs:531`). No session type declares `[JsonExtensionData]` (none exists anywhere in `src_mcp`, `src_server`
or `shared`), and the context's option set (`src_mcp/src/Server/ServerJsonContext.cs:346-351`) leaves
`UnmappedMemberHandling` at its default, *skip*. So every member that a NEWER server added is gone after one save by an
older one.

This happened to every field added since the first release: `RoundRecord.Caller`, `Size`, `Gitlinks`, `PlanSource`
(E5 §7 item 18 lists them), and E5's coming authors snapshot. E5 decided that the database is the durable copy for its
own field, and gave the general guard to a separate change (E5 §7 item 18). **This plan is that change.**

**Measured on today's code (2026-10-18, a throwaway test, deleted before commit):** a session file with one unknown
member at the root, in `state`, in a round (`authors`) and in a reviewer row was loaded and saved through
`SessionStore`. All four members were gone. The assertion said:
`Expected root.TryGetProperty("futureRoot", out _) to be True because root unknown member, but found False.`

## 2. What the code does today (verified)

### 2.1 The one read path and the one write path

- Read: `SessionStore.Load` (`SessionStore.cs:461-478`) and `SessionStore.TryRead` (`:658-668`), both through
  `Normalised` (`:696-716`). `Normalised` uses `with` copies only (`:703`, `:715`).
- Write: `SessionStore.Save` → `Write` (`:501-560`); the JSON is made once at `:531`. Nothing else in `src_mcp`
  serialises `PersistedSession`.
- The extension only READS session files (`src_vs_code/src/extension.ts:1756-1774`,
  `src_vs_code/src/panelProvider.ts:4723-4741`). It never writes one.

### 2.2 The records in the file

| Type | Where | Only in the session file? | Grew after the first release |
|---|---|---|---|
| `PersistedSession` | `SessionStore.cs:266-368` | yes | `Caller`, `FeatureBase`, `Carried`, `UsedPrompts`, `Plan`, `Epic` … |
| `SessionState` | `core/Rounds/SessionState.cs:382-500` | yes | `HoldQuestions`, `RequestQuestions`, `SecondRound`, `Document`, `Feature` |
| `PanelConfig` | `core/Rounds/SessionState.cs:65-252` | yes | `SecurityLane` (2026-10-01, `100b4b67`) |
| `RoleGate` | `core/Rounds/SessionState.cs:48` | yes (settings build it from the environment, `PanelSettings.cs:1591-1615`) | `Enabled` (2026-09-08, `f3b47141`) |
| `RoundRecord` | `SessionStore.cs:78-263` | **no** — also in the `open`/`status` answer (`ServerJsonContext.cs:150-158`) | `Caller`, `Size`, `Gitlinks`, `PlanSource`, (E5) `Authors` |
| `ReviewerState` | `SessionStore.cs:40-65` | **no** — inside `RoundRecord`, so also in the answer | `Seconds`, `Model`, `Effort`, `Warning` |
| `Finding` | `core/Findings/Finding.cs:54` | **no** — `ReviewAnswer`, `SessionAnswer.Pending`, `--findings` | out of scope (§4 D7) |
| `PriorRejection`, `CarriedDecision` | `core/Gate/GateRule.cs:7`, `Server/Rounds/CarriedDecision.cs:19` | hold a `Finding` | out of scope (§4 D7) |
| `CallerDeclaration`, `RoundSize`, `PlanSource` | `Server/CallerDeclaration.cs:34`, `core/Context/RoundSize.cs:52`, `core/Rounds/PlanSource.cs:18` | no — in the answer through `RoundRecord`, and projected to the database | out of scope (§4 D7) |

### 2.3 Copies and rebuilt records

- Every change to a loaded session, state or round is a `with` copy: `SessionStore.cs:626`, `:630`, `:703`, `:715`;
  `RoundSkips.cs:97`; `RoundEngine.cs:241`, `:592`; `PanelService.cs:1741`; `AskHumanService.cs:365`;
  `core/Rounds/RoundMachine.cs:319-327` (`FreshFeatureReview`).
- A record is built with `new` only where THIS process makes it: a new session (`PanelService.cs:499-500`,
  `:1161-1162`; `Stages/FeatureStage.cs:202`), the running and the finished round (`LiveRound.cs:269`), a skipped round
  (`RoundSkips.cs:102`), reviewer rows of the running round (`LiveRound.cs:88`, `:202`). None of these replaces a record
  that an older file supplied, except the running round that the same process wrote seconds before.
- `RoundEngine.cs:241` replaces the session's `SecurityLane` gate with the one from the settings. That is a decision of
  the running server, not a loss; a bag on that `RoleGate` goes with it (§4 D6).

### 2.4 Members that are written but never read

`RoundRecord.LoggedNote` (`SessionStore.cs:163`), `SessionState.IsDocumentSession` (`core/Rounds/SessionState.cs:481`)
and `IsFeatureSession` (`:499`) are get-only. They are in every file today. `PanelConfig.Catalog` carries `[JsonIgnore]`
(`core/Rounds/SessionState.cs:127-128`) and is never written.

## 3. What the throwaway proof showed (2026-10-18)

A test file in this worktree (`src_mcp/tests`, deleted before the plan's commit) ran against `main` and then against a
TEMPORARY copy of `SessionStore.cs` and `SessionState.cs` with a bag on `PersistedSession`, `SessionState`,
`RoundRecord` and `ReviewerState`. Both source files were restored with `git checkout --` before any commit.

| # | Question | Result |
|---|---|---|
| M1 | Today: unknown members after load + save | **Lost** at all four levels (the RED in §1). Also through a `with` copy. |
| M2 | Bag with an `init` accessor (public or `internal`) | **Build green, runtime failure** at the first deserialisation: `InvalidOperationException: The extension data property 'Unknown' on type 'CoaiMcp.Core.Rounds.SessionState' cannot bind with a parameter in the deserialization constructor.` The source generator makes an `init` member an object-initializer parameter. `Load` catches only `JsonException`, `IOException` and `UnauthorizedAccessException` (`SessionStore.cs:474`), so this would break every `open`. |
| M3 | Bag with `private set` + `[JsonInclude]` | **Compile error** SYSLIB1038 (the generator cannot see a private setter). |
| M4 | Bag with `internal set` + `[JsonInclude]` | **Works.** Also for `SessionState` in `CoaiMcp.Core`, because that assembly gives its internals to `coai-mcp` (`src_mcp/core/CoaiMcp.Core.csproj:74`; `src_mcp/src/CoaiMcp.csproj:5`). |
| M5 | M4: unknown members after load + save, and through `with` copies of session, state and rounds | **Kept**, with the same values. |
| M6 | M4: a file with no unknown member | Every bag is `null`; the root members written are exactly the type info's property names (no new member). |
| M7 | M4: get-only members (`loggedNote`, `isDocumentSession`, `isFeatureSession`) with stale values in the file | **Not** put in the bag; written once, with the computed value. |
| M8 | M4: a known member in another case (`"PlanText"`, `"PLANTEXT"`) | Binds to the known property; never in the bag; written once. |
| M9 | M4: a bag that holds a KNOWN name (`planText`), put there by code | Written **twice**; on the next read the bag's stale copy **wins** (last member wins). Deserialisation never makes this (M7, M8) — only code can. |
| M10 | M4: `SessionAnswer` built from the loaded rounds | The answer **echoes** the bags (`authors`, the reviewer member). With `r with { Unknown = null }` on rounds and reviewer rows, it does not. |
| M11 | Record equality | Two reads of one file were never equal records: `List<ReviewerState>` compares by reference. The bag changes nothing here. |
| M12 | Native AOT | `dotnet publish src_mcp/src/CoaiMcp.csproj -c Release -r win-x64` with M4: **zero warnings** (warnings are errors, `Directory.Build.props:8`), binary linked. The binary was **not run**. |

Side finding, not this plan's: a hand-made file without `"rejections"` loads with a default `ImmutableArray` and the
next save throws `NullReferenceException` (the generator gives an absent `init` member its default, not its
initializer). Every real file has the member: `Rejections` exists since the first state machine (`b51db3af`,
2026-08-31). The tests below therefore put `"rejections": []` in every fixture.

A precedent in the family: `dew_flow_creds_for_devs · src_minimalapi_server/src/OrgMembers.cs:145-160` (checked at
`a73b7c12`) puts `[JsonExtensionData] IDictionary<string, JsonElement>? Unknown { get; set; }` on a positional record
for the same reason and records the same `init` failure; its test is
`src_minimalapi_server/tests/OrgMembersStoreTests.cs:274`. That project uses a PUBLIC setter; this plan uses a
narrower one (D2).

## 4. Decisions

- **D1 — A bag on every type whose only JSON home is the session file, plus the round and its reviewer rows.** That is
  `PersistedSession`, `SessionState`, `PanelConfig`, `RoleGate`, `RoundRecord`, `ReviewerState` (§2.2). Every type in
  that list grew after the first release. The round and the reviewer row are in the answer too; D5 handles that.
- **D2 — The shape:** `[JsonExtensionData, JsonInclude] public Dictionary<string, JsonElement>? Unknown { get; internal set; }`
  in the record body, never a constructor parameter. `init` fails at run time (M2) and `private set` does not compile
  (M3). The XML comment says: only the deserialiser, the save scrub (D3) and the answer strip (D5) set it; nothing
  mutates the dictionary in place, because `with` copies share it. `null` when the file had no unknown member (M6),
  so files and answers stay as they are today.
- **D3 — The save scrubs every bag, in ONE place.** `SessionStore.Write`, before `:531`, makes a scrubbed copy: from
  each non-null bag it drops (a) every key that names a member of that type's JSON contract, case-insensitive, from
  `ServerJsonContext.Default.<Type>.Properties` (get-only members included), and (b) every key on a per-type RETIRED
  list, empty at first. A scrub never changes the dictionary it was given: it builds a new one on a `with` copy (the
  caller's snapshot stays as it was). A bag that ends empty becomes `null`. Why both, when deserialisation never puts
  a known name in a bag (M7, M8): (a) closes M9 for code that sets a bag, and (b) is the one place a release removes a
  member it retires — without it a retired member lives in the bag for ever (§8).

  **The copy is rebuilt from the leaves up** (plan gate, 2026-10-18), because `with` is shallow and a scrubbed child is
  useless unless its parent is re-made to hold it:
  1. each `ReviewerState` of each round → `v with { Unknown = Scrub(v.Unknown, …) }`;
  2. each `RoundRecord` → `r with { Unknown = …, ReviewerStates = [.. the scrubbed rows] }`;
  3. each `RoleGate` in `State.Config.Roles` → a new dictionary of scrubbed gates, and `State.Config.SecurityLane`;
  4. `State.Config` → `config with { Unknown = …, Roles = …, SecurityLane = … }`;
  5. `State` → `state with { Unknown = …, Config = … }`;
  6. the session → `session with { Unknown = …, State = …, Rounds = [.. the scrubbed rounds] }`.

  A level whose bag is `null` and whose children are unchanged is passed through as the SAME object, so a session with
  no bag anywhere costs one walk and no allocation of records. T6 plants a forged key at EVERY level, not only the root.
- **D4 — No other behaviour changes.** The option set (`ServerJsonContext.cs:346-351`) stays. `UnmappedMemberHandling`
  stays *skip* (an extension-data property and *disallow* do not go together).
- **D5 — The `open`/`status` answer carries no bag.** `SessionAnswerFor` (`PanelService.cs:1881-1894`) maps each round
  to `r with { Unknown = null, ReviewerStates = [.. r.ReviewerStates.Select(v => v with { Unknown = null })] }`. The
  caller gets the members this server knows, as today (M10). One small static method beside `SessionAnswerFor`; E5's
  own `Authors` strip (E5 S1) uses the same seam when it lands (§5).
- **D6 — What a bag does not promise.** It keeps unknown members of a record that this process READ and wrote back.
  A record that this process builds new (§2.3) has an empty bag; today no such record replaces one that an older file
  supplied, except the running round this process wrote itself. The `SecurityLane` gate that `RoundEngine.cs:241`
  replaces from the settings loses its bag, by design: the running server decides the gate.
- **D7 — `Finding` and the shared value types stay out (open tail).** `Finding` is a wire type in `ReviewAnswer`,
  `SessionAnswer.Pending` and the `--findings` modes; a bag on it would reach every one of those answers. It is NOT
  short-lived in the file: `RoundMachine.cs:664` adds rejected findings to `SessionState.Rejections`. So a member a
  newer server adds to `Finding` is still lost from `pending`, `rejections` and `carried` after an older re-save. The
  same is true for `CallerDeclaration`, `RoundSize` (and its nested sizes) and `PlanSource`. The fix for them, if it is
  ever needed, is a persistence projection (a session-only record with a bag, mapped to the wire type), as the creds
  precedent does for its wire shape — a separate plan. This plan says "the six types", never "the whole file".
- **D8 — Honest limit: the guard helps only releases AFTER it.** Every `coai-mcp` released before this change still
  drops unknown members. A window that runs such a binary strips the file as before. The guard starts to help when every
  server that shares the data directory runs the release that carries it. The release note says this.

## 5. The boundary with E5 (both sides)

| Item | Who builds it | The other one's part |
|---|---|---|
| The bag on `RoundRecord` and the five other types (D1, D2) | **this plan** | E5 adds `Authors` as a KNOWN member; nothing in E5 depends on the bag |
| The save scrub (D3) | **this plan** | none |
| The answer strip of the bags (D5) | **this plan** | E5 S1 strips `Authors` from the same answer; whichever lands second puts its strip in the same method |
| `Authors` stripped by servers released BEFORE this plan | nobody can: those binaries are out (D8) | E5's database copy and E7's repair stay the answer for them |
| The authors snapshot, step 24, the card line | **E5** | none |

Order: either can land first; they touch `RoundRecord` and `SessionAnswerFor` in different lines. Disjoint: nothing
else. **The same table is written into E5** — its §8 on `main`, added by this plan's branch (plan gate, 2026-10-18).
E5's own branch (`feat/round-authors-e5`, `c1216b42`) rewrote that document and ends at §11; when it rebases, it keeps
that section and points its §7 item 18 at it (§10).

## 6. Stories

### S1 — The RED test, then the bags

1. Add `src_mcp/tests/ASessionFileKeepsWhatANewerServerWroteTests.cs` with the RED tests of §7.1 (T1, T2). Run them on
   `main`: they must fail with the message in §1. Record the message in the commit body.
2. Add the bag (D2) to the six types (D1). Run T1–T5: green.
3. **The AOT gate, before S2 starts** (plan gate, 2026-10-18 — M12 built and linked the binary but did not run it). Publish
   (`dotnet publish src_mcp/src/CoaiMcp.csproj -c Release -r win-x64` into a scratch folder) and run THAT binary once:
   - `COAI_DATA_DIR` set to a new temp folder (`PanelSettings.cs:874-880`; the server uses a side folder under it,
     `ResolveDataDir`, so run once on the empty folder and plant the file in the `sessions/` folder it creates);
   - a session file planted at the name `SessionStore.FileFor` gives, with unknown members at every level of T1;
   - `initialize` and one `tools/call open` over stdio, by a small script in the scratch folder (not committed), on a
     throwaway repository (a temp `git init` with one commit — `open` prunes the repository it is given,
     `PanelService.cs:491`);
   - then read the file back. Prediction: every unknown member is still there, with the same value, and the `open`
     answer holds none of them once S3 has landed (before S3 it holds them — M10).

   The vault is not needed for this: a vault that cannot be read is a recorded note (`ProvidersAnswer.VaultRead`,
   `ServerJsonContext.cs:103`), not a refused start. If the binary still cannot be run, S1 is NOT done and S2 does not
   start: the suite runs the JIT build and cannot stand in for the published artefact. The installed `coai-mcp` and the
   live data directory are never used. The observation goes in the commit body.

### S2 — The save scrub

`SessionStore` gains a private static `Scrubbed(PersistedSession)` and a static `RetiredMembers` table (type → names,
empty), used by `Write` before `:531` (D3, rebuilt from the leaves up). Tests T6–T8 and T10.

### S3 — The answer strip

`PanelService.SessionAnswerFor` maps rounds as in D5. Test T9 through `OpenAsync` and `StatusAsync`.

### S4 — Docs and the measured run

- `research/module_server.md` § *Persistence* (`:2014-2020`): one paragraph — the bags, the scrub, the strip, D6, D7
  and D8, and that `init` fails at run time (M2).
- The rule for a release that retires a member of a bagged type: add its name to `RetiredMembers` and update T10's
  list in the same commit.
- After S3, the S1 AOT run once more on the final build, to see the `open` answer without bags.

## 7. Test plan

All in `src_mcp/tests`, run by the MTP executable (`node .agents/conventions/tools/low-priority.mjs
./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe`). Fixtures are planted at `SessionStore.FileFor`
(`SessionStore.cs:725`) and always carry `"rejections": []` (§3, side finding).

### 7.1 The RED (on today's code)

- **T1 `AnOlderServersReSave_KeepsEveryMemberItDoesNotKnow`** — unknown members at the root, in `state`, in
  `state.config`, in one `roles` gate, in a round (`authors` with nested numbers and arrays) and in a reviewer row; load,
  save, parse the file: each member is there with the same value. **RED on `main`** (measured, §1).
- **T2 `AWithCopy_KeepsTheUnknownMembers`** — as T1, but saved after `with` copies of the session, the state and each
  round. **RED on `main`** (measured).

### 7.2 Green after S1–S3

- **T3 `AFileWithNoUnknownMember_HasNoBag_AndWritesWhatItWroteBefore`** — every bag is `null`; the member names at each
  level equal that type's `ServerJsonContext.Default.<Type>.Properties` names (M6).
- **T4 `AComputedMember_IsWrittenOnceWithItsComputedValue`** — stale `loggedNote`, `isDocumentSession`; each written once,
  computed value (M7). Green on `main` too: a guard against a later change.
- **T5 `ANewlyKnownMember_BindsAndIsWrittenOnce`** — a member in another case binds to the known property and is written
  once (M8). This is the case "a later version ADDS the member an older file holds as unknown".
- **T6 `AKnownNameInABag_IsDroppedOnSave_AndTheTypedValueWins`** — a forged known name in the bag of EVERY level (session
  `planText`, state `stage`, config `onExhausted`, a gate `threshold`, a round `verdict`, a reviewer row `status`), each
  holding a stale value; save, reload: every typed value wins and each name appears once; the other unknown members are
  still there (M9). One forged level alone would not see a scrub that forgets to re-make a parent (D3).
- **T7 `ARetiredMember_IsDroppedOnSave`** — the test registers one retired name through an `internal` seam; save drops
  it and keeps the rest.
- **T8 `TheScrubLeavesTheSnapshotItWasGivenUnchanged`** — after `Save`, the caller's bag still holds the forged key
  (no in-place change).
- **T10 `EveryBaggedTypesMembers_AreTheListedOnesOrRetired`** — a committed list of each bagged type's JSON member names;
  the test compares it with `ServerJsonContext.Default.<Type>.Properties`. A member that disappears from a type fails the
  test unless its name is in `RetiredMembers`; a new member fails it until the list gains it. This is the check that a
  release which retires or RENAMES a member also tells the scrub (plan gate, 2026-10-18). A rename matters most: the old
  name in old files becomes a bag entry and the new property starts empty.
- **T9 `OpenAndStatus_CarryNoUnknownMember`** — through `OpenAsync` and `StatusAsync` on a repository built as
  `ACodeRoundRecordsItsSizeTests` builds one (`:159`), with a planted file: the answers contain none of the unknown
  member names, and the file still holds them after `open` saved it (M10).

### 7.3 Break-its (each one goes red, then is restored)

| Break | Red test |
|---|---|
| Remove the attribute from `RoundRecord` | T1 (`authors` gone) |
| Make the setter `init` | every test that loads a file — `cannot bind with a parameter` (M2) |
| Remove the scrub call in `Write` | T6 (`planText` twice, `OLD` wins) |
| Scrub the given dictionary in place | T8 |
| Remove the answer strip | T9 |
| Remove `[JsonInclude]` and keep `internal set` | T1 (the generator ignores the property; the bag stays empty) |
| Scrub only the root bag (no rebuild of the children) | T6 (the round's `verdict` copy wins) |
| Remove a member from `RoundRecord` and leave `RetiredMembers` as it is | T10 |

The whole suite runs after S3; the count of passed tests is in the commit body.

## 8. Growth

A bag holds only what a newer server wrote into that record, so a file is never larger than the newest server made it.
One case grows nothing new but stops shrinking: a member that a later release RETIRES stays in old files' bags for ever,
unless that release adds the name to `RetiredMembers` (D3). T10 fails that release until it does; the rule is also in
`module_server.md` (S4).
Nothing here has an in-flight state, so no sweep is needed.

## 9. Other files with the same exposure (open tail — not fixed here)

The question was asked of every JSON file `coai-mcp` reads and writes again. Checked: the source-generated contexts in
`src_mcp`, and each store's write call. None has an extension-data bag.

| File | Read and rewritten? | Where |
|---|---|---|
| Escalation questions | yes — read, changed, written | `src_mcp/src/Server/Escalations.cs:236-243` |
| Consultation records | yes — `with` copies written back | `src_mcp/src/Server/Consultation/ConsultationStore.cs:49`, `:235-236` |
| Question-consultant records | yes | `src_mcp/src/Server/QuestionConsult/QuestionConsultStore.cs:115-122` |
| Question-phase state | yes | `src_mcp/src/Server/QuestionConsult/QuestionPhaseStore.cs:173` |
| Cadence state | yes | `src_mcp/src/Server/Cadence/CadenceStore.cs:98-104` |
| Consultant check state | yes | `src_mcp/src/Server/Consultation/ConsultCheckState.cs:321` |
| The usage ledger | **no** — append-only JSONL, a line is never rewritten | `src_mcp/runners/Reviewers/UsageLedger.cs:571` |
| Settings and vendor rows (`COAI_VENDORS`, `COAI_ROLES`) | **no, on the server side** — the server only reads them (`PanelSettings.cs:1309`, `:1364`, `:1548`); the extension writes them | the extension's own write path was NOT checked |
| Run markers, tracked processes, review-tree records, consult health | written by their owner; whether an older server rewrites a newer one's record was NOT checked | `RunMarkers.cs:171`, `runners/Processes/ProcessTracking.cs:58`, `runners/Worktrees/ReviewTreeRecord.cs:170`, `Consultation/ConsultHealth.cs:227` |

Each "yes" row has the same loss as the session file had. They are recorded, not fixed: no measured field loss is known
there today, and each needs its own D7-style look at where its records are sent. A later plan takes them by evidence.

## 10. Questions left

1. The boundary is now on both sides on `main`'s line (E5 §8, this branch). E5's branch (`feat/round-authors-e5`)
   rewrote E5 and belongs to another session; this plan did not edit that branch. When it rebases it must keep E5 §8
   and point its §7 item 18 at it — a text conflict at the end of the file is likely, and the resolution is to keep both.
2. Whether `Finding` and the other shared value types (D7) need the projection plan is open. It needs a measured loss
   first — a member added to `Finding` that a user missed.

## 11. Definition of Done

- [x] T1 and T2 were RED on `main` with the §1 message, and the message is in the commit body.
- [x] The six types carry the bag of D2, `internal set` + `[JsonInclude]`, with the comment that names who sets it.
- [x] `SessionStore.Write` scrubs known and retired names on a copy, rebuilt from the leaves up (D3); `RetiredMembers`
      exists and is empty; T10's member list is committed.
- [x] `open` and `status` carry no bag (D5); T9 proves it through the service.
- [x] T1–T10 green; every break-it in §7.3 went red and was restored; the whole suite run, with its count (PR #868).
- [x] The PUBLISHED AOT binary ran on a planted file before S2 started (S1 step 3) and again after S3; no suite run
      stands in for it.
- [x] `research/module_server.md` § *Persistence* says what the bags keep, what they do not (D6, D7), and D8.
- [ ] The release note says the guard helps only releases after it (D8). It is in PR #868's body; the release pull
      request that ships it writes it into `src_vs_code/CHANGELOG.md` (deviation 8).
- [x] `node .agents/conventions/tools/plan-lifecycle.mjs` is clean, and this plan is promoted when the work ships.
