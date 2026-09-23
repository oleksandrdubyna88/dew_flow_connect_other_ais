# PLAN — the split order names the caller's OWN strongest model, and the person picks it

> Status: **IMPLEMENTED, 2026-09-23.** Scope: the third gate switch
> (`splitWithFable`) — `GateCommands.FableCommand`, the settings seam that carries it, the panel's
> *The gate* section, its help, and the bench check that reads it. Issue #117. Nothing is left open.
>
> Related: [PLAN_commands_and_autonomy.md](PLAN_commands_and_autonomy.md) (the design record
> of the three switches), [module_server.md](module_server.md),
> [module_extension.md](module_extension.md).

## What shipped differently from this plan

Gate: plan round 1 `good_enough` (2 of 3 reviewers; codex failed on its configured model), code round 1
`good_enough` (8 of 12; the four codex reviewers failed the same way). Three reviewers of our own ran
beside it.

- **Pickers are `<select>`s through `modelsFor`, not text boxes with a `<datalist>`** (step 8). The
  panel had already recorded why (`panelView.ts` `modelOptions`): a datalist filters its options by the
  value in the box, so once a model is chosen every other one vanishes. "another model…" posts a new
  panel command, `customCommandModel` (`<kind>:<slot>`), and the host asks for the name. The list is
  `modelsFor` on the kind's runtime (`COMMAND_MODEL_RUNTIMES`; `other` has none), so Claude's families
  carry the labels its CLI answered — the first build hand-picked lists and the code review caught it.
- **The shipped pairs are held level by `shared/command-models.json`**, not by
  `panelServerDefaultsAgreement.test.ts` reading C# (step 3 of the build order): a test reading the other
  program's source is what `NothingReadsAnotherProgramsSourceTests` exists to stop. The same file holds
  `orderOpensWith`, the marker, because the BENCH holds a third copy of it and references no server code.
- **`FableCommand` became `ModelCommand`** (not `StrongestModelCommand`), and the marker is the phrase the
  old sentence already began with, `"Do the SPLIT itself with "` — plan round 1 (gemini) showed a separate
  marker sentence could not coexist with the byte-for-byte Claude text.
- **The setting is parsed by its own record, `CommandModelsSetting`**, beside `ConsultantsSetting`, rather
  than a `Key.CommandModels` inside `PanelSettings`.
- **Two defects the code review found, each fixed red-first:** the host rebuilt the page's message from
  four hand-listed fields and dropped `commandModel`, so no picker choice was ever stored (now
  `settingMessageFrom`, tested with a page's real message); and `ToDictionary` threw on two spellings of
  one kind (`Codex`/`codex`) outside the `JsonException` catch (now the indexer, last one wins).
- **Added, not planned:** pairs are compared without case (picking the CLI alias `fable` is the default);
  a kind the build does not know is never written (the `__proto__` refusal); the run-the-page harness was
  extracted to `test/panelPageHarness.ts`, reads hyphenated `data-*` names as a DOM does, and has tests of
  its own; the second picker's label is "Implementation", not "For implementation".

## The symptom

The panel's third switch reads *"Split with Fable, and give it the risky stories"*. Ticked, every plan
round that orders a split hands the calling AI this order (`src_mcp/core/Commands/GateCommands.cs:129-134`):

> Do the SPLIT itself with Fable at its highest available version … Then implement: ordinary stories
> on Opus, and anything where being wrong is expensive … on Fable (max) again.

Two things are wrong with that, and the owner named both (issue #117, 2026-09-08):

1. **The models are not a choice.** "Fable" and "Opus" are string literals. There is no way to say
   "use this other model for the hard half" or "implement on Sonnet".
2. **The order is the same whoever is calling.** The command is carried out by the CALLING AI — the
   gate never runs these models itself (the doc comment at `GateCommands.cs:6-14` records why a
   Fable *reviewer* check was wrong). A Codex session told "use Fable and Opus" has neither; it gets an
   order it cannot follow. The owner wants Codex models (e.g. Astra) and Google's to be choosable.

Decided with the owner on 2026-09-23: **one pair of models per CALLER KIND** — the shape the
*Consultant* section already has (`src_vs_code/src/consultSettings.ts:23-28`, `CALLER_KINDS`). A Claude
Code caller is told Claude models, a Codex caller Codex models, and so on. Defaults stay Fable (the
strongest) and the latest Opus (implementation) for Claude Code.

## What must be true when it is done

1. With the switch on and nothing else changed, a **Claude Code** caller receives an order naming
   Fable for the split and the risky stories, and Opus for ordinary stories — the same models as today.
2. The panel lets a person set, **per caller kind** (Claude Code, Codex, Gemini, another client), the
   *strongest* model and the *implementation* model. Each box offers the models this machine knows for
   that vendor (Claude families, the discovered Codex list, the Antigravity/Gemini list) and accepts any
   typed name.
3. A **Codex** (or Gemini, or other) caller receives an order naming the models set for ITS kind. With
   nothing set for that kind, the order says "the strongest model your client offers" / "your usual
   model" and names **no** Claude model.
4. Only a caller kind whose pair differs from the shipped pair crosses to the server
   (`COAI_COMMAND_MODELS`, one JSON key) — a pristine panel writes nothing new, per the rule
   `panelServerDefaultsAgreement.test.ts` enforces.
5. A value the server cannot read is reported as an unrecognised setting (the page shows it, like every
   other) and the shipped pair is used — the order is advice, so an unreadable setting does not refuse
   the round.
6. With an installed `coai-mcp` older than the one that reads the key, the panel says so beside the
   boxes: that server will name Fable and Opus to every caller whatever the boxes show.
7. The switch's label, tooltip and help say "the strongest model", not "Fable"; the stale help sentence
   *"when a Fable vendor is configured"* (`src_vs_code/src/helpContent.ts:148` and its four translations)
   is gone.
8. The bench's settings check (`src_bench/CoaiBench/Running/SettingsApplied.cs:89,128`) still detects
   the order when the switch is on — for any caller kind.

## Constraints

- **The stored key stays `coai.splitWithFable` / `COAI_SPLIT_WITH_FABLE`.** Renaming it is a migration of
  every stored settings file and a skew with every older server, for a word nobody sees. Only the label
  changes; the key's doc comment says why the name is historical.
- `CoaiMcp.Core` stays pure: the caller kind is resolved in `PanelService`, and the command receives the
  two model names, never an environment.
- No new file under the data directory (so `shared/data-inventory.json` is untouched), no new wire
  field on a round's answer — the order is still one string in `Commands`.
- UI text in English. The page is tested by running it (`.agents/PROJECT.md`), never by matching its
  source text.
- The gate granularity sentence (*"After EVERY story: call review_code"*) is **not** touched here — that
  is issue #131's plan. Boundary below.

## Boundary with the neighbouring plans

| Item | Built by | The other plan's part |
|---|---|---|
| Per-caller model names in the split/model order | **this plan (#117)** | — |
| The split sizes (stories only / 2–3 epics / …) and "one gate per epic / per task" | #131's plan | reads the same `CommandContext`; does not touch model names |
| Editable command prompts, custom commands, export/import of settings | #467's plan | will turn this command's text into a template; the `{strongest}` / `{implementation}` values this plan introduces become its placeholders, and `coai.commandModels` is one of the settings it exports |

Order: this plan first (it is the smallest and lands the per-caller seam); #131 next; #467 last.
Disjoint otherwise.

## Design

### Server (`src_mcp`)

1. **`CommandModels`** (new, `src_mcp/core/Commands/CommandModels.cs`, pure): a record
   `ModelPair(string Strongest, string Implementation)`, the shipped map
   `{ claude: ("Fable", "Opus"), codex: ("",""), gemini: ("",""), other: ("","") }`, and
   `For(map, kind)` — configured, else shipped, else `other`'s — the lookup `ConsultantRouting.For`
   already does (`src_mcp/src/Server/Consultation/ConsultantRouting.cs:83-86`).
2. **Parse** `COAI_COMMAND_MODELS` in `PanelSettings.From` beside the three flags
   (`src_mcp/src/Server/PanelSettings.cs:700-702`), through a DTO in `SettingsJsonContext`
   (Native AOT — no reflection). Malformed JSON → shipped map + one `UnrecognisedSetting`
   (key `Key.CommandModels`), the shape `ConsultantRouting.Parse` uses (`ConsultantRouting.cs:88-111`)
   minus the refusal. Unknown caller kinds are kept (a newer panel may know more).
3. **`CommandContext`** gains `ModelPair Models` (default = the Claude pair, so every existing test
   and caller that does not set it gets today's text). `PanelService` fills it at
   `src_mcp/src/Server/PanelService.cs:1321` from `CallerIdentity.KindFrom(Environment…)` — the same
   kind the consultant is chosen by (`src_mcp/src/Server/CallerSessions.cs:87-102`). `KindFrom` never
   answers blank: a client that exports none of the three session variables is `other`
   (`CallerSessions.cs:101`), and `CommandModels.For` maps any kind it has no row for to `other`'s
   pair — so an unidentified client is never told Claude models. (Plan round 1, local and gemini.)
4. **`FableCommand` becomes `StrongestModelCommand(ModelPair)`** — ONE template whose Claude
   instance is today's sentence byte for byte (plan round 1, gemini: a separate marker sentence
   would have contradicted that). With `S` and `I` the two resolved names:

   > Do the SPLIT itself with **{S}** at its highest available version — deciding what the epics and
   > stories are is the judgement that shapes everything after it. Then implement: ordinary stories on
   > **{I}**, and anything where being wrong is expensive — payments, money, authentication, security,
   > architecture, data migration — on **{S}** (max) again. Name the model you used for each story in
   > your summary.

   `S = "Fable"`, `I = "Opus"` is exactly `GateCommands.cs:129-134`. **Per-field resolution**, so a
   pair with one name set is never half-blank: a configured name, else the shipped name of that kind
   and field, else the generic words — `S` → *"the strongest model your client offers"*,
   `I` → *"your usual model"*. (With a generic `S` the sentence still reads: "with the strongest model
   your client offers at its highest available version".) The bench's marker is **"Do the SPLIT itself
   with"** — a phrase every variant, the old one included, already starts with.
5. The existing log line `split ordered to caller {Caller}` (`PanelService.cs:1354`) gains the caller
   kind and the two names, so a log reader can see which models were ordered.

### Extension (`src_vs_code`)

6. **Setting `coai.commandModels`**: `{ [callerKind]: { strongest, implementation } }`, declared in
   `package.json` beside the three switches (~`:562-576`), added to `OVERLAID_SETTINGS`
   (`settingsShape.ts:289-306`) so a side can have its own, parsed key by key (a record holding one
   kind still answers for the others — `asRoleFlags`' rule).
7. **`envBlock`** (`settingsShape.ts:427-435`) writes `COAI_COMMAND_MODELS` with only the kinds that
   differ from the shipped pair.
8. **Panel**: under the third checkbox in `gateBody` (`panelView.ts:1185-1192`), a four-row block —
   caller kind, *Strongest*, *For implementation* — two text inputs per row, each with a `<datalist>`
   of that vendor's known models (`CURATED_CLAUDE_MODELS`, the discovered Codex list,
   `ANTIGRAVITY_MODELS`/discovered agy list via `modelsFor`, none for *another client*). The write goes
   through the panel's existing record-update path (the `consultantRecordUpdate` pattern,
   `src_vs_code/src/consultantWrite.ts:65`), so a field another panel wrote is not deleted. A box
   cleared (or holding only whitespace) REMOVES that field from the stored record, so the kind falls
   back to its shipped name for that field; a kind left with no fields drops out of the record.
9. **Skew note**: `commandModelsSkewNote(installedVersion, settings)` — the pattern of
   `consultantSkewNote` (`consultSettings.ts:462-479`): shown only when the installed server is known,
   strictly older than `COMMAND_MODELS_SINCE` (the next minor after `mcp 0.32.0`, i.e. `0.33.0`), and
   some kind differs from the shipped pair.
10. **Words**: the label becomes *"Split with the strongest model, and give it the risky stories"*;
    `help.ts:25`, `package.json:572` `markdownDescription`, `helpContent.ts:148` and
    `help{Ru,De,Es,Uk}.ts:88` say "the strongest model you choose per caller" and lose the stale
    "Fable vendor" clause.

### Bench (`src_bench`)

11. `SettingsApplied.WithFable` (`:89`) becomes the marker from step 4 (`"Do the SPLIT itself with"`);
    the expectation text at `:128` says "an order naming the strongest model".

## Growth

None: one settings key, bounded by four caller kinds × two short strings. No table, file or process.

## Build order

1. Server: `CommandModels` + parse + `CommandContext.Models` + the command — tests first (below).
2. Server: `PanelService` wiring + the log line.
3. Extension: settings shape, env block, defaults agreement.
4. Extension: the panel block, the write path, the skew note, words and help.
5. Bench marker.
6. Docs: `research/module_server.md` (commands section), `research/module_extension.md` (the gate
   section), `src_vs_code/CHANGELOG.md` `## Unreleased`, `README.md` if it names the switch.

## Test plan

**C# (`CoaiMcp.Tests`)**
- `GateCommandsTests`: the Claude pair renders today's sentence **byte for byte** (a regression pin
  written BEFORE the refactor, run red-free against the old code, then kept green through it);
  a Codex pair names its two models and contains neither "Fable" nor "Opus"; an empty pair renders the
  generic words and no Claude model; a pair with ONE name set uses it for that field and the fallback
  for the other; every variant starts with the marker.
- `CommandModelsTests`: `For` falls back configured → shipped → `other`, per field; an unknown kind
  resolves as `other`; `Parse` of malformed JSON gives the shipped map and exactly one unrecognised
  setting naming `COAI_COMMAND_MODELS`; valid JSON of the wrong SHAPE (a number for a field, an array
  for the map, `null`) is the same — shipped map plus a sentence, never an exception; an unknown kind is
  kept; whitespace is trimmed.
- `SettingsAreLiveTests`: a `COAI_COMMAND_MODELS` written to the settings file governs the NEXT call.
- End to end (the `SplitOrderTests` shape): a round run with `CODEX_SESSION_ID` set and a Codex pair
  configured returns an order naming that pair.

**TypeScript (`npm test`)**
- `settingsShape`: pristine → no `COAI_COMMAND_MODELS`; one changed kind → only that kind on the wire;
  a partial stored record reads the shipped pair for the rest.
- `panelServerDefaultsAgreement`: the panel's shipped pairs are the server's.
- `settingsAreDeclared` / `settingsReach` / `helpCoverage`: the new key is declared, reaches the env,
  has help.
- The panel page, RUN (the `bundledPage` harness): the block draws four rows; typing into a box posts
  the write the host expects for that kind and field; the skew note appears for an older version and
  not for a current one.
- `commandModelsSkewNote` unit tests: unknown version → nothing; older + differing → the sentence;
  older + pristine → nothing.

**Bench (`CoaiBench.Tests`)**: `SettingsAppliedTests` with the new marker, both for the Claude words and
a Codex pair.

**Whole suites** before the pull request: `CoaiMcp.Tests.exe`, `CoaiBench.Tests`, `npm test`,
`node .agents/conventions/tools/plan-lifecycle.mjs`.

## Definition of Done

- [ ] A Claude Code caller's order is unchanged word for word with the pristine settings.
- [ ] A person can set both models per caller kind, and a caller of that kind is told those names.
- [ ] A caller with nothing configured is never told a model of another vendor.
- [ ] Only differing kinds cross; an unreadable value is reported and falls back.
- [ ] An older installed server is named beside the boxes.
- [ ] Label, tooltip, `markdownDescription`, help in five languages say "strongest model"; the stale
      "Fable vendor" clause is gone.
- [ ] The bench recognises the order for every caller kind.
- [ ] Tests above written and green; whole suites run; module docs and CHANGELOG updated.
- [ ] The plan's completion check ran and this plan was promoted.
