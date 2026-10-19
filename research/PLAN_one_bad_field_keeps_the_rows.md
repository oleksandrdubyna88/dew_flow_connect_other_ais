# PLAN — One bad field in one reviewer row costs that row only, and the server says which row and which field

> Status: **IMPLEMENTED, 2026-10-18 (S1–S3; branch `docs/plan-one-bad-field-keeps-the-rows`, not merged or released at promotion).** Scope: the `COAI_VENDORS` parse in coai-mcp
> (`PanelSettings.ParseVendors` and `VendorListFrom`), one new small reader file beside it, the existing
> "unrecognised setting" notice path, tests, and the docs. No extension code. Owner approval: 2026-10-18.
> Plan gate: round 1 `good_enough` (session 3e591978, 3 of 4 reviewers; kimi timed out) — 6 findings applied
> (D1 name from the raw element, D3 any refused row configures, D5 `MemberOf`, D7 per-row key), 3 rejected with
> reasons checked in the code (`SecurityLaneSetting.Apply` appends; a consultant row is always one element; the
> extension's `unrecognised` parse takes any non-empty string).
>
> **What shipped differently** (code gate round 1 `proceed`, 3 gating findings under a threshold of 5, 8 of 16 reviewers
> answered, session 3e591978; owner defaults taken: Q1 `maxPromptKib` unchanged, Q2 every row refused → no reviewer):
> 1. **Names.** `VendorRows(Read, Refused, Complaint)` with a derived `IsList`, and `VendorList.Said` (the finished
>    `UnrecognisedSetting` list from `VendorRowReader.SaidAbout`) — not `IsList` as a member and `VendorList.Refused`.
> 2. **`PanelSettings.cs` grew by 7 lines, but about 150 moved**: the DTO → row mapping was extracted, unchanged inside,
>    into `MapVendors`, so `ReadVendors` can hand it the rows that read. The split plan moves it as one block.
> 3. **T3's assertions are split** into `ANullRow_IsLeftOut_AndTheSettingsStillRead` and `ANullRow_IsSaid_ByItsPlace`.
> 4. **An extra commit** pins T10 (the consultant row) and T11 (out-of-range numbers) beside the reader.
> 5. **The sentence.** D5's "wrong JSON type" also covers a number that does not fit (`30.5`, `99999999999` for an `int?`);
>    the parser does not say which. D5's cure ("Write a number or true/false without quotes") was wrong for `"model":5` and
>    `"id":5`, so the judge's review changed it to *"… has a value of the wrong JSON type, or a number it cannot hold. …
>    Write text in quotes, numbers and true/false without them, and minutes as whole numbers from 1 to 1440"* (the range
>    came from a code-gate finding). The not-a-list sentence (D4) names `COAI_PROVIDERS`, which is what runs when it is
>    set, not only the shipped pair.
> 6. **B2** (the `ValueKind` check) is not needed for correctness — the deserializer answers null for a null row — but it
>    gives the exact sentence, and T3b pins it.
>
> **Open tail** (named, not built here): `PanelSettings.Detail` is shared by `ParseRoles` and the reader — the split plan
> decides its home; `COAI_PROMPTS_PER_ROUND` still loses its whole map silently (§ 9); Q1 stays an owner question.
>
> Related: [PLAN_a_reviewer_is_not_sent_more_than_it_can_read.md](PLAN_a_reviewer_is_not_sent_more_than_it_can_read.md)
> (its deviation 3 found this; `maxPromptKib` stays as it shipped), [PLAN_panel_settings_is_too_big.md](../todo/PLAN_panel_settings_is_too_big.md)
> (the boundary is in § 8, and named on its side too), [module_server.md](module_server.md).

## 1. The symptom

A judge on PR #856 found this. It is verified on `origin/main` at `2b302c65e`.

`COAI_VENDORS` is a JSON list of reviewer rows. `PanelSettings.ParseVendors` reads the WHOLE list with one call:
`JsonSerializer.Deserialize(json, SettingsJsonContext.Default.ListVendorDto)` (`src_mcp/src/Server/PanelSettings.cs:1364`).
`VendorDto` (`src_mcp/src/Server/SettingsJsonContext.cs:30-78`) has typed members: `bool?` for `plan`, `code`,
`document`, `feature`, `thinking`, `stream`; `int?` for `reviewMinutes`, `timeoutMinutes`; a nested `PriceDto` of
`double?` and `long?` (`:89-96`); and 11 `string?` members. When ONE member of ONE row has the wrong JSON type, the
deserializer throws `JsonException`, and the catch returns an empty list (`PanelSettings.cs:1416-1419`).

Measured with a probe that uses the same source-generated options (2026-10-18):

| Member as written | Result for the WHOLE list |
|---|---|
| `"timeoutMinutes":"30"` | `JsonException` — zero rows |
| `"timeoutMinutes":30.5` | `JsonException` — zero rows |
| `"timeoutMinutes":30.0` | `JsonException` — zero rows (a whole number with a decimal point is refused too) |
| `"timeoutMinutes":99999999999` | `JsonException` — zero rows |
| `"plan":"false"` | `JsonException` — zero rows |
| `"stream":1` | `JsonException` — zero rows |
| `"model":5` | `JsonException` — zero rows |
| `"price":{"in":"3"}` | `JsonException` — zero rows |
| `"maxPromptKib":"180"` | reads — 2 rows (the `JsonElement?` of #856) |
| `[null, {"id":"b"}]` | reads, with a NULL row — then `.Where(v => !string.IsNullOrWhiteSpace(v.Id))` (`PanelSettings.cs:1368`) reads `Id` of null and throws `NullReferenceException`, which no catch takes |

**What the person sees today.** Not "no reviewers": the judge's sentence is true for `ParseVendors` itself, but the
caller makes it worse in a quieter way. `VendorListFrom` (`PanelSettings.cs:1307-1317`) treats an empty result from a
non-empty text as "not configured" (`:1316`: `vendors.Count > 0 || IsEmptyList(json)`), so `WithProvidersFrom`
(`:1289-1297`) runs `DefaultProviders` (`:1296`) — `COAI_PROVIDERS`, or the shipped pair **codex and antigravity**.
So:

1. Every row the person configured stops reviewing. Two reviewers the person may never have chosen review instead.
2. Nothing is logged. Nothing goes into `UnrecognisedSettings` (`PanelSettings.cs:964-976`), so the MCP `providers`
   tool and `--providers` (`PanelService.cs:255-259`, field `unrecognised`), the startup and reload notice
   (`StartupNotices.cs:69-76`, `PanelServiceHost.cs:140`) and the panel toast `server-did-not-understand-a-setting`
   (`src_vs_code/src/panelProvider.ts:934-963`) all say nothing.
3. The panel draws the person's own rows from `coai.vendors`. The server does not report them, so each card is
   `unknown` (`src_vs_code/src/providers.ts:287-297`), which by design says nothing.
4. A `null` row is worse: `FromEnvironment` throws, at startup and on every settings reload (`PanelServiceHost.cs:109`).

The same parser reads ONE row in two more places: a consultant's catalog row (`ConsultantResolver.OptionsOf`,
`src_mcp/src/Server/Consultation/ConsultantResolver.cs:178-192`, call at `:187`) and the `--check-model` row on
stdin (`ConsultantCheckMode.RowsOf`, `ConsultantCheckMode.cs:172-186`, call at `:180`). A bad member there makes the
row unreadable, and both refuse by name today. That is correct, and this plan keeps it (D6).

## 2. Who can write a bad type

**The extension cannot, through its own writer.** Checked:

- `vendorsFrom` (`src_vs_code/src/vendors.ts:401-446`) rebuilds every row from `coai.vendors` and keeps a member only
  when its type is right: strings by `typeof`, `plan`/`code` as `!== false` (always a boolean), `document` only when
  `typeof === 'boolean'`, rates through `rate()` (`:528-530`, finite and `> 0`).
- `catalogFields` (`src_vs_code/src/catalogFields.ts:36-50`): `timeoutMinutes` only through `isMinutes`
  (`apiSettings.ts:190-192`: an integer, 1 to 1440), `maxPromptKib` only through `isKib` (`promptCap.ts:16-18`: an
  integer, 1 to 4096), `stream` only when `=== true`, `fast` only `on`/`cli`.
- `storedApiSettings` (`apiSettings.ts:166-182`): `effort` must match `LEVEL`, `thinking` must be a boolean,
  `reviewMinutes` must pass `isMinutes`.
- `rowOnTheWire` (`src_vs_code/src/vendorsWire.ts:121-146`) is a whitelist of those typed fields, and `vendorsEnv`
  (`:95-107`) writes them with `JSON.stringify`, which never writes `30.0` or `"30"` for a number.

So a hand edit of `coai.vendors` in VS Code's `settings.json` is cleaned before it crosses. One theoretical gap: the
price tier's `tierFrom` comes from a third-party LiteLLM list (`modelPrices.ts:198-217`, `Number(threshold) * 1000`). A
threshold above about 9.2 × 10^15 thousand tokens would overflow `long?` (`SettingsJsonContext.cs:93`). No real list
has one. Not fixed here; § 9 names it.

**Who CAN write one:** a person who edits the server's own settings file in the data folder by hand (the file the
extension writes, `settingsShape.ts:476`), and a script or container that sets `COAI_VENDORS` directly. The panel
rewrites the file at its next save, so the bad row lasts until then — or forever, for a script.

## 3. Other parses with the same shape

The question asked: which other settings or catalog parses lose EVERYTHING for one bad member? Each line is checked.

| Parse | File:line | One bad member gives | Said? |
|---|---|---|---|
| `COAI_VENDORS` — this plan | `PanelSettings.cs:1359-1420` | zero rows → the shipped pair | **No** |
| `COAI_PROMPTS_PER_ROUND` | `PanelSettings.cs:1244-1267` (`ParsePromptRounds`) | the whole map empty: no per-round prompt choice | **No** |
| `COAI_ROLES` | `PanelSettings.cs:1538-1559` (`ParseRoles`) | no rows → the shipped roles | Yes — `WhyRoles` (`:1231-1238`) |
| `COAI_CONSULTANTS` | `Consultation/ConsultantRouting.cs:89-110` | the shipped map, consulting refused | Yes — `Complaints` |
| `COAI_COMMAND_MODELS` | `CommandModelsSetting.cs:28-47` | the shipped models | Yes — `Complaints` |
| `COAI_COMMANDS` | `CommandsSetting.cs:43-62` | no custom command (`CustomCommandDto.Enabled` is `bool?`) | Yes — `Complaints` |
| `COAI_QCONSULT_ROWS` | `src_mcp/core/QuestionConsult/QuestionRow.cs:75-94` | no rows, the question consultant refuses | Yes — `Complaints` |
| `COAI_QCONSULT_PROMPTS` | `src_mcp/core/QuestionConsult/QuestionPromptSet.cs:61-80` | no custom prompt | Yes — `Complaints` |
| Team server `vendors.json` | `src_server/src/Vendors/VendorCatalog.cs:117-133` | the file refused, the previous catalog kept | Yes — the refusal |

The six "said" parses are out of scope: each is visible, and most of them have only string members. The two silent
ones are `COAI_VENDORS` (this plan) and `COAI_PROMPTS_PER_ROUND`. The second is out of scope (§ 9): the extension
writes it, its values are only strings, and losing it changes which prompts run, not who reviews.

## 4. The goal

1. A row that does not read is left out, ALONE. Every other row reviews as configured.
2. The server says which row and which member, through the notice path that already exists. No new channel, no
   extension change.
3. A row the person wrote is never replaced by a reviewer the person did not choose.
4. A `null` or non-object row never stops the server.
5. Everything that reads today reads exactly as today: same rows, same values, no sentence.

## 5. The options, and the choice

**(a) Read each typed member as a `JsonElement?`, and keep the row with that member unset** — the `maxPromptKib`
pattern (`SettingsJsonContext.cs:69-74`, `PromptCapOf` at `PanelSettings.cs:1492-1495`).
Rejected, for four reasons:
- **It fails open on consent.** `plan` and `code` are TRUE when absent (`PanelSettings.cs:1385-1386`). A hand-written
  `"plan":"false"` would become "reviews plans" — the opposite of what the person wrote. `document` absent is "the
  plan tick" for a local row (`ProviderSettings.Serves`), so `"document":"no"` could send a company document to a
  reviewer.
- **Unset is not always harmless.** An unset `timeoutMinutes` is the round's timeout, which can be LONGER than the
  person's limit (a consultant pointed this out; checked: `RosterBuilder.TimeoutFor`, `ARowsOwnTimeoutTests.cs:31-38`).
- **It does not cover everything.** The 11 string members, the nested `price`, and a `null` or non-object row still
  throw. Covering them needs every member as a `JsonElement?` — about 20 member readers.
- **It is silent.** Each reader would need to carry a sentence out of the mapping.

**(b) Parse row by row: a row that does not read is left out and named; the others survive.** **Chosen.**
- One loop covers every member of every type, the nested price, and null and non-object rows.
- The bad row fails CLOSED: it does not review. That is the rule the consultant row already follows
  (`ConsultantResolver.cs:174-176`: "refused by name, never a quiet fall back").
- `JsonException.Path` names the member (`$.timeoutMinutes`, `$.price.in`). The row is named by its `id` when the
  `id` reads, else by its position.
- It reuses the existing `VendorDto` and its source-generated type info. It adds NO member-level mechanism.

**(c) Both** — rejected. (a)'s part adds only one thing that (b) does not: the bad row keeps reviewing, with the
member unset. That is the fail-open outcome above. Two mechanisms for one problem is also what the task said not to
build.

**`maxPromptKib` stays as #856 shipped it** (`JsonElement?`, a bad value is "no cap", the row is kept, nothing is
said). Tests pin it (`ARowCarriesAPromptCapTests.cs:43-49`, `:86-89`). It never throws, so (b) never sees it. Making
it "said" is a question for the owner (§ 10, Q1), not part of this change.

**The notice: reuse `UnrecognisedSettings`.** It already reaches all three places: the `providers` answer
(`unrecognised`), the startup and reload notice with the log line, and the panel toast. `COAI_ROLES` already puts
one sentence per dropped row into it (`PanelSettings.cs:967`); this plan does the same for `COAI_VENDORS`, with one
key per row (D7).

## 6. Decisions

Each is checked against the code. Two consultants answered the open questions on 2026-10-18 (consult
`a672dedabed24f3cac544b521ac4a11e`); both chose (b) and both chose D3. Their claims were checked in the files named.

- **D1 — The reader.** A new file `src_mcp/src/Server/VendorRowReader.cs`, `internal static class VendorRowReader`.
  It parses the text with `JsonDocument`. When the root is an array, it deserializes each element with
  `SettingsJsonContext.Default.VendorDto` (the source generator already makes this type info, because
  `List<VendorDto>` is registered at `SettingsJsonContext.cs:143`) inside its own `try`/`catch (JsonException)`. A
  `null` or non-object element is refused by a `ValueKind` check, not by an exception. It answers a record:
  `VendorRows(IReadOnlyList<VendorDto> Read, IReadOnlyList<VendorRefusal> Refused, bool IsList)`, where
  `VendorRefusal(string Name, string Sentence)`.
  **The name of a refused row comes from the raw `JsonElement`, never from the DTO** (plan gate, gemini and vibe): when
  deserialization throws there is no DTO. The reader walks the element's properties (`EnumerateObject`, name compared
  `OrdinalIgnoreCase`, because the context reads case-insensitively, `SettingsJsonContext.cs:140`) and takes `id` only
  when it is a non-blank string. It never calls `GetProperty`, which throws on a missing member. With no usable `id`,
  the row is named by position: `row 2 of 3`.
  It does NOT map to `ProviderSettings`: the one mapping stays in `ParseVendors` (`PanelSettings.cs:1365-1414`), so
  there is no second copy. A new file, because `PanelSettings.cs` is 1712 lines against a ceiling of 800 (§ 8).
- **D2 — `ParseVendors` keeps its signature.** It has about 40 callers, all in tests, and two in the consultant code.
  A new `ReadVendors(json, places, presence)` answers rows AND refusals; every `ParseVendors` overload becomes
  `[.. ReadVendors(…).Rows]`. The outer `catch (JsonException)` stays only for a text that is not JSON at all.
- **D3 — "Configured" when every row is left out.** Today, empty result + non-empty text = not configured = the
  shipped pair. New rule in `VendorListFrom` (`PanelSettings.cs:1316`): the list is configured when a row was read,
  OR the text is `[]` (as today), OR **any row was refused** — whether or not its `id` could be read (`[null]`,
  `[5]`, `[{"id":5}]` included). Then the provider list is empty, and the round refuses with its own sentence, as for
  `[]` (`AnEmptyReviewerListIsEmptyTests.cs:25-29`). Reason: a refused row is a row the person WROTE; running codex and
  antigravity in its place is the failure #688 already fixed for `[]` (goal 3; plan gate, codex).
  Unchanged: rows that READ but carry no usable `id` (`[{}]`, `[{"id":"  "}]`) are not refused — they are skipped as
  today, say nothing, and still run the shipped pair, as `AnEmptyReviewerListIsEmptyTests.cs:31-36` pins (#688,
  CodeRabbit: "rows with no usable id are not a choice of nobody").
- **D4 — A text that is not JSON at all keeps its fallback, and is now SAID.** `VendorSettingsTests.cs:50-56` pins
  that `[{oops` runs the shipped pair ("a review run against a vendor list somebody half-wrote is worse than the
  default one"). That stays. New: one sentence with the parser's position, through `Detail(e)` (`PanelSettings.cs:1567`),
  the helper `ParseRoles` already uses. A root that is JSON but not an array (`{}`, `"x"`) is treated the same way.
- **D5 — The sentence.** One per refused row, no `Kind` (so it goes into `unrecognised`, not `settingProblems`):
  `COAI_VENDORS: the row "claude" (row 2 of 3) is left out of every round — its member timeoutMinutes has a value of the wrong JSON type. The other rows still review. Write a number or true/false without quotes, or save the reviewers in the panel again, which rewrites the list.`
  For a null or non-object row: `… row 2 of 3 is left out — it is null, not a row …` (or `… it is a number, not a row …`).
  The member comes from ONE helper, `MemberOf(string? path)`: `JsonException.Path` is nullable, so a null or blank
  path, or a bare `$`, gives `one of its members`; otherwise the leading `$.` (or `$`) is removed and the rest is kept
  as written (`timeoutMinutes`, `price.in`, or the bracket form `['odd name']` that System.Text.Json uses for a name it
  cannot write with a dot). No `Substring` on an unchecked value (plan gate, gemini and vibe).
  The parser's own message is NOT copied: it names a CLR type
  (``System.Nullable`1[System.Int32]``), which helps nobody. The row id is lower-cased and trimmed, as the parser
  makes it (`PanelSettings.cs:1369`).
- **D6 — The consultant row and `--check-model` keep their refusal.** Both call `ParseVendors("[<row>]")` and refuse
  when they do not get exactly one row (`ConsultantResolver.cs:187`, `ConsultantCheckMode.cs:180-182`). Under (b) a bad
  member still gives zero rows, so both still refuse by name. Their sentences do not change in this plan (§ 9).
- **D7 — Where the sentences go.** `VendorList` (`PanelSettings.cs:1300`) gains `Refused`. `WithProvidersFrom`
  appends them: `UnrecognisedSettings = [.. UnrecognisedSettings, .. list.Refused.Select(r => new UnrecognisedSetting($"COAI_VENDORS[{r.Name}]", r.Sentence))]`
  — the shape `SecurityLaneSetting.Apply` already uses (`SecurityLaneSetting.cs:42-43`). `FromEnvironment` then runs
  `SecurityLaneSetting.Apply`, which appends its own lane complaints and does not replace the list
  (checked, `SecurityLaneSetting.cs:35-44`), so the sentences survive, once each.
  **The key is per ROW** (`COAI_VENDORS[claude]`, `COAI_VENDORS[row 2 of 3]`), not the bare variable name. The notice
  ledger groups repeats on (code, subject), and the subject is the key (`StartupNotices.cs:74-75`; the reason is in
  `PanelSettings.cs:612-616`). Under one shared key, two bad rows would fold into ONE notice row on the notices page
  (plan gate, vibe). The panel toast is not the problem: it already shows every sentence, joined, in one toast
  (`panelProvider.ts:937-963`). `UnrecognisedSetting.Key` is read only for that grouping and for `SettingProblems`,
  which takes only entries with a `Kind` (`PanelSettings.cs:634-635`); these have none.
- **D8 — No extension change.** The toast, the `unrecognised` parse (`providers.ts:129-136`) and the startup notice
  exist. The toast's cure ("Fix them in ConnectOtherAIs Settings and reload the window") is right for a panel user;
  the sentence itself names the hand-edit cure.

## 7. Stories and build order

One pull request, one commit per story, each green on its own. Build order: **S1 → S2 → S3.**

- **S1 — The row reader (D1, D2, D5).** `VendorRowReader.cs`; `ReadVendors` and the `ParseVendors` overloads
  delegate to it; the `null`-row crash goes away. Red first: T1, T3.
- **S2 — The notice and the configured rule (D3, D4, D7).** `VendorList.Refused`; the `VendorListFrom` rule;
  `WithProvidersFrom` appends the sentences; the not-JSON sentence. Red first: T2, T4, T5, T6.
- **S3 — Docs.** `research/module_server.md`: a new section after § *A row's prompt cap* (`:7066`), "One bad field
  costs its row only", with the rule, the sentence and the configured rule; § *A CLI row's own timeout* (`:7059`) gets
  one line that a wrong type now drops the row and says so. A row in the flow catalogue of `research/module_tests.md`
  (`:156`): "a hand-edited reviewer row with a wrong type" — covered by T1–T7; NOT covered: a real hand-edited file
  in a real editor. One line under deviation 3 of
  [PLAN_a_reviewer_is_not_sent_more_than_it_can_read.md](PLAN_a_reviewer_is_not_sent_more_than_it_can_read.md)
  that points here.

## 8. Boundary with PLAN_panel_settings_is_too_big.md (named on both sides)

| Item | This plan | [PLAN_panel_settings_is_too_big.md](../todo/PLAN_panel_settings_is_too_big.md) |
|---|---|---|
| The per-row read of `COAI_VENDORS` | builds it, in the NEW file `VendorRowReader.cs` | does not touch it |
| `ParseVendors` and the DTO → row mapping | changes only its call to the reader (a few lines) | moves it to `VendorSettingsReader.cs` (its step 3) |
| `VendorListFrom`, `VendorList`, `WithProvidersFrom` | adds `Refused` and the configured rule | moves them with the vendor block |
| `PanelSettings.cs` size | grows by about 10 lines | brings it under 800 |

Order: either can go first. This plan is a behaviour fix and is small; whichever lands second rebases onto the
other. Disjoint otherwise: this plan touches no other block of `PanelSettings.cs`.

## 9. Out of scope — named, not silently left

- `COAI_PROMPTS_PER_ROUND` loses its whole map, silently, for one bad value (`PanelSettings.cs:1244-1267`). The
  extension writes it; only a hand edit breaks it. A separate small plan, if the owner wants it.
- `maxPromptKib` stays "no cap, not said" for a bad value (§ 5). Q1.
- Out-of-range values of the RIGHT type (`"timeoutMinutes": 0`, `1441`) stay "unset, not said", as
  `ARowsOwnTimeoutTests.cs:19-29` pins.
- The consultant and `--check-model` refusals keep their wording (D6). They could name the member; not here.
- A row with a blank `id` is still dropped without a sentence (`VendorSettingsTests.cs:44-47`), as today.
- The theoretical `tierFrom` overflow from a third-party price list (§ 2). Under this plan it costs one row, said —
  no longer the whole list.

## 10. Questions left for the owner

- **Q1.** Should a bad `maxPromptKib` also be SAID (the row kept, the cap unset, one sentence)? Today it is silent,
  by #856's decision. Default if unanswered: no change.
- **Q2.** D3 changes one outcome: a list in which any row is refused, and no row reads, now runs no reviewer (the
  round refuses, and the sentences say why) instead of the shipped pair. Both consultants and the plan gate (codex)
  preferred this. Default if unanswered: D3 as written.

## 11. Growth

No new growth surface. The sentences live in memory with the settings. The notice ledger already folds repeats by
code and subject (`StartupNotices.cs:72-75`), and `COAI_ROLES` already sends one sentence per dropped row under one
key. No state machine, so no sweep.

## 12. Test plan

Run as the MTP executable, never `dotnet test`:
`node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe`.
New tests go in `src_mcp/tests/OneBadFieldCostsItsRowOnlyTests.cs`, in the `VendorSettingsTests.cs` shape
(`From(env)` → `PanelSettings.FromEnvironment`).

**Red first, on today's code.** Each test is written and run against `origin/main` BEFORE the fix, and the red text
is reported:

| # | Test (named for the guarantee) | Red today, because | Green after |
|---|---|---|---|
| T1 | `AWrongTypeInOneRow_CostsOnlyThatRow` — `ParseVendors("""[{"id":"claude","runtime":"claude","timeoutMinutes":"30"},{"id":"gemini","runtime":"gemini"}]""")`; a theory over `"30"`, `30.5`, `30.0`, `"plan":"false"`, `"model":5`, `"price":{"in":"3"}`, `"stream":1`, `"document":"no"` | **zero vendors** (`Expected … to be equal to {"gemini"}, but found empty`) | exactly `gemini` |
| T2 | `AWrongTypeInOneRow_IsSaid_ByRowAndMember` — `COAI_VENDORS` as in T1, through `FromEnvironment` | `Providers` is `codex, antigravity`; `Unrecognised` is empty | `Providers` is `gemini`; `UnrecognisedSettings` has one entry, key `COAI_VENDORS`, its sentence names `"claude"` and `timeoutMinutes` |
| T3 | `ANullRow_IsLeftOutAndSaid_AndTheSettingsStillRead` — `[null,{"id":"gemini","runtime":"gemini"}]` | `NullReferenceException` out of `FromEnvironment` | `gemini`; a sentence naming row 1 of 2 |
| T3b | `ARefusedRowWithNoId_StillRunsNoReviewerThePersonDidNotChoose` — `[null]`, `[5]`, `[{"id":5}]`, `[{"runtime":"claude","timeoutMinutes":"30"}]` | `NullReferenceException` for `[null]`; `codex, antigravity` for the rest | `Providers` empty (D3), one sentence naming `row 1 of 1` |
| T4 | `EveryRowLeftOut_RunsNoReviewerThePersonDidNotChoose` — `[{"id":"claude","runtime":"claude","timeoutMinutes":"30"}]` | `codex, antigravity` | `Providers` empty, the sentence present |
| T5 | `AListThatIsNotJson_StillFallsBack_AndIsNowSaid` — `[{oops` and `{}` | the fallback is right, but `Unrecognised` is empty | the shipped pair (as `VendorSettingsTests.cs:50` pins) AND one sentence with the parser's position |
| T12 | `TwoBadRows_AreTwoNotices` — two rows with a wrong type; `StartupNotices.Unrecognised` with a recording `Noticing` | (no sentence today) | two notes, subjects `COAI_VENDORS[claude]` and `COAI_VENDORS[row 2 of 3]`, each with its own sentence |
| T13 | `AMemberPathIsSaidPlainly` — `MemberOf` over `null`, `""`, `"$"`, `"$.timeoutMinutes"`, `"$.price.in"`, `"$['odd name']"` | (new helper) | `one of its members`, three times; then `timeoutMinutes`, `price.in`, `['odd name']` |
| T6 | `ProvidersCarriesTheRowAndTheMember` — `PanelService.ProvidersAsync` with the T2 settings (the `ARowCarriesAPromptCapTests.cs:91` shape) | `unrecognised` is empty | `unrecognised` holds the T2 sentence |

**Guards that must stay green, unchanged** (regression):

| # | What | Where |
|---|---|---|
| T7 | A clean list reads the same rows and says nothing; the `providers` answer is byte-identical | new, in the same file |
| T8 | `maxPromptKib` `"180"` and `180.5`: the row is kept, no cap, its neighbour survives, no sentence | `ARowCarriesAPromptCapTests.cs:43-49`, `:86-89`, plus a no-sentence assertion in the new file |
| T9 | `[]` is empty; `[{}]` and `[{"id":"  "}]` run the shipped pair | `AnEmptyReviewerListIsEmptyTests.cs:25-43` |
| T10 | A consultant row with a wrong type is still refused by name (`OptionsOf` answers null) | new, beside `AConsultantIsHandedItsRowsKeyTests.cs` |
| T11 | Out-of-range numbers of the right type are unset, as today | `ARowsOwnTimeoutTests.cs:19-29` |

**Break-its** (change production, name the test that must go red, restore, run green again — reported with the red
text):
B1 — let a per-row `JsonException` escape to the outer catch → T1, T2.
B2 — remove the `ValueKind` check for a `null` row → T3, T3b.
B3 — put back `vendors.Count > 0 || IsEmptyList(json)` as the configured rule → T4.
B4 — do not append the refusals to `UnrecognisedSettings` → T2, T6.
B5 — make the configured rule need a readable `id` on a refused row → T3b.
B7 — key every refusal as the bare `COAI_VENDORS` → T12.
B8 — read the refused row's name with `GetProperty("id")` → T3b (`KeyNotFoundException`).
B6 — drop the not-JSON sentence → T5.

The extension suite (`npm test` in `src_vs_code`) is not run for a change: no extension file changes (D8).

## Definition of Done

- [ ] T1, T3, T3b, T4, T12 each watched RED on today's code with the symptom in the message (zero vendors, the shipped
      pair, the `NullReferenceException`), then GREEN; T2, T5, T6 the same.
- [ ] B1–B8 run, each red with the named test, restored, green.
- [ ] T7–T11 green without change to their assertions.
- [ ] The full `CoaiMcp.Tests` executable green; `dotnet format` clean on the touched files.
- [ ] `VendorRowReader.cs` under 800 lines; `PanelSettings.cs` grows by no more than about 10 lines.
- [ ] `research/module_server.md`, `research/module_tests.md` and the #856 plan's deviation 3 updated (S3).
- [ ] The boundary table is in this plan AND in [PLAN_panel_settings_is_too_big.md](../todo/PLAN_panel_settings_is_too_big.md).
- [ ] Q1 and Q2 answered by the owner, or the defaults recorded as taken.
- [ ] Plan gate `proceed`, code gate `proceed`; then this plan promoted to `research/` with its deviations.
