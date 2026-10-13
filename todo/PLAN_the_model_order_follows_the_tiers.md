# PLAN — the split order follows the subagent model tiers: Fable splits and specs the cornerstones, Opus judges, Sonnet writes

> Status: **plan only, nothing implemented yet, 2026-10-13.** Scope: the *Split with the strongest model* order
> (`shared/commands/command-model.md`, `GateCommands.ModelCommand`), the per-caller model slots it is filled from
> (`CommandModels`, `COAI_COMMAND_MODELS`, the extension's `coai.commandModels` and its pickers), the help in five
> languages, and the releases of coai-mcp and the extension. The Team server renders no order and is not touched.
>
> Related: [PLAN_the_caller_names_its_strongest_model.md](../research/PLAN_the_caller_names_its_strongest_model.md)
> (issue #117 — the two slots this plan widens to three), [PLAN_commands_and_autonomy.md](../research/PLAN_commands_and_autonomy.md)
> (the switches), [module_server.md](../research/module_server.md), [module_extension.md](../research/module_extension.md).
> Across the boundary: `dew_flow_conventions · todo/PLAN_subagent_model_tiers.md` §1 (D1–D12) is the canonical
> statement of the policy; this plan only makes the gate's ORDER say the same thing.

| Item | Built by | The other plan's part |
|---|---|---|
| The policy itself — `common/subagent-models.md`, the guard hook, the epic/story layout in `planning-docs.md`, the behaviour smoke | `dew_flow_conventions · todo/PLAN_subagent_model_tiers.md` | none — this plan only reads its §1 |
| The gate's *Split with the strongest model* ORDER, its three slots, the panel pickers, the help, the coai releases | this plan | none — the conventions plan does not touch coai's order |

Order: the conventions plan's §1 decisions come first (they are the source); this plan follows them and may ship
before the conventions rule does — the order is advice either way. Disjoint otherwise. The conventions plan is being
written by another session and is not edited from here; the mirror row there is owed by its author.

## 1. The symptom

With *Split with the strongest model* on, a first plan round hands the caller this order (Claude Code, nothing
configured), verbatim:

> Do the SPLIT itself with Fable at its highest available version — deciding what the epics and stories are is the
> judgement that shapes everything after it. Then implement: ordinary stories on Opus, and anything where being wrong
> is expensive — payments, money, authentication, security, architecture, data migration — on Fable (max) again. Name
> the model you used for each story in your summary.

The operator replaced that policy on 2026-10-13 (conventions plan D1–D12). The order now contradicts it in five places:

| The order says | The policy says |
|---|---|
| Fable splits into epics AND stories | Fable splits the plan into **epics**, after asking the **question consultant** what matters and where; **Opus** splits each epic into **stories**, one stories document per epic |
| ordinary stories are implemented on Opus | **Opus** writes the test scenarios and the brief and reviews; **Sonnet** writes the code, fixes and unit/integration tests; the main session writes only a change to ONE file of ≤ 50 changed lines |
| risky stories are implemented on Fable (max) | **cornerstone** stories — at most ⌈2·E/5⌉ per plan of E epics — and **security** work (cryptography, authentication, authorisation, outside the quota) get their **spec** from Fable, review from Opus, code from Sonnet |
| — | search and research go to **Sonnet scouts**, ⌈1.5·N⌉ where N would have run on Opus, each with its own focus |
| "at its highest available version", "(max)" | **aliases only**, never a versioned id; Fable refused on a spend limit → continue on Opus and say so; the summary names models **and counts** |

The order names TWO models; the policy has THREE tiers. So this is not a text edit: the third tier needs a slot.

## 2. What must be true when it is done

1. A Claude Code caller with nothing configured gets an order that states every row of §1's right column, naming
   Fable, Opus and Sonnet — and still opens with `Do the SPLIT itself with ` (`GateCommands.ModelOrderMarker`, the
   bench's marker, `shared/command-models.json` `orderOpensWith`).
2. The order is filled from THREE per-caller slots: `strongest`, `implementation` (unchanged names on the wire and in
   the stored setting) and a new `coder`. Shipped: Claude Code `Fable` / `Opus` / `Sonnet`; codex, gemini, other —
   nothing, as today.
3. A caller kind with nothing named is told generic words for every slot and **never another vendor's model**:
   "the strongest model your client offers" / "your usual model" / "a faster model your client offers". No
   half-blank sentence ("on  writes").
4. Per-FIELD fallback stays: a person who set only `coder` keeps the shipped strongest and implementation, and the
   reverse. `COAI_COMMAND_MODELS` rows without `coder` (every extension released so far) resolve `coder` to the shipped
   one; an OLD server given a row WITH `coder` ignores the field (System.Text.Json drops unmapped members — the DTO
   has no `UnmappedMemberHandling`), so the halves can ship out of step.
5. The extension draws a third picker per caller kind (*Code and search*), writes it as `coder` through the existing
   `commandModel` write path, sends it in `COAI_COMMAND_MODELS`, and declares it in `package.json`'s
   `coai.commandModels` schema. A panel whose `coder` differs from the shipped one, beside a server older than the
   release that reads it, shows a skew sentence — the `COMMAND_MODELS_SINCE` shape.
6. The editable text keeps working: `{coder}` is a documented placeholder (`CommandTexts` remarks, the commands
   page's placeholder list in `commands.ts`), and a person's override of `command-model` is still honoured verbatim.
7. The help describes the three tiers in all five languages (EN `helpContent.ts`, DE/ES/RU/UK), the `splitWithFable`
   hint and both `package.json` descriptions say three models, and a test asks each language for the new picker's
   English label, as the existing "sides seen" test does.
8. The sibling split-size orders (`command-split-*`) are unchanged — they say how many epics and stories, not who
   makes them, and do not contradict the policy.

## 3. Constraints

- Never name a Claude model to a non-Claude caller (issue #117's rule) — the per-kind map stays the only source.
- The order is advice: an unreadable `COAI_COMMAND_MODELS` still falls back to the shipped map with one complaint.
- `ModelPair` keeps its type name on both sides (C# record, TS interface): renaming it touches ~15 files for no
  behaviour, and its fields are what the wire and the stored setting key on. Its doc comment says it holds three.
- `TheOrdersAreWhatTheyWereTests` is re-recorded with `COAI_RECORD_GATE_COMMANDS=1`, and the fixture's diff must
  touch the model orders and nothing else.
- No new one-shot mode, no new environment key, no wire change on the Team server.

## 4. Build order

1. **Core (C#).** `ModelPair(string Strongest = "", string Implementation = "", string Coder = "")`
   (`src_mcp/core/Commands/CommandModels.cs:6`); `ClaudeCode = new("Fable", "Opus", "Sonnet")` (`:25`); `For` resolves
   the third field per field (`:49-58`). `GateCommands.ModelCommand` (`src_mcp/core/Commands/GateCommands.cs:275`)
   fills `{coder}` with its generic fallback. `CommandTexts` remarks list `{coder}` (`CommandTexts.cs:18`).
2. **The text.** `shared/commands/command-model.md` rewritten to §1's right column with `{strongest}`,
   `{implementation}`, `{coder}`; `shared/command-models.json` gains `coder` per kind.
3. **Server setting.** `CommandModelDto` gains `Coder` (`src_mcp/src/Server/SettingsJsonContext.cs:124`);
   `CommandModelsSetting.Rows` carries it (`CommandModelsSetting.cs:69`).
4. **Extension.** `commandModels.ts`: the slot list, `SLOT_LABELS` (`strongest` *Strongest*, `implementation`
   *Briefs and review*, `coder` *Code and search*), `SHIPPED_COMMAND_MODELS`, `NO_MODELS`, `commandModelsFrom`,
   `resolvedPair`, a `CODER_SLOT_SINCE = '0.51.0'` and the skew sentence for it; `panelView.ts:1687` a third box;
   `commands.ts:64` the placeholder list; `scripts/generate-command-texts.mjs` regenerates
   `commandTexts.generated.ts`; `package.json` schema + descriptions.
5. **Help.** `help.ts:75`, `helpContent.ts:149` and the matching paragraph in `helpDe.ts`, `helpEs.ts`, `helpRu.ts`
   (`:112`) and `helpUk.ts` (`:48`).
6. **Docs.** `research/module_server.md` (§ the model order, ~4847-4871, and the placeholder list ~6407),
   `research/module_extension.md` (~8143-8166), an `## Unreleased` section in `src_vs_code/CHANGELOG.md`.
7. **Compatibility, measured.** `src_vs_code/scripts/live-command-models-compat.mjs`, the shape of
   `live-security-settings-compat.mjs`: a `COAI_COMMAND_MODELS` row WITH `coder` handed to the released mcp 0.50.0
   and to the new build through `--providers`; neither may list it as unrecognised. Run once, result in the PR.
8. **Release** after the merge: mcp (minor — `feat:`), then the extension; the release PR gets its
   `## Server 0.51.0` / `## Extension 0.72.0` heading before it is squash-merged; the changelog-baseline row for mcp
   follows in its own PR. The Team server is not released.

## 5. Test plan

- `GateCommandsTests`: the byte-for-byte Claude Code test is replaced by one that pins the NEW order's key clauses —
  question consultant before the epic split, epics → stories on Opus, one stories document per epic, the cornerstone
  quota ⌈2·E/5⌉, spec Fable / review Opus / code Sonnet, the 50-line exception, scouts ⌈1.5·N⌉ on Sonnet, aliases
  never versioned ids, the Fable-refused fallback, models and counts in the summary — and keeps "payments" and
  "security". A codex pair with three names gets all three and no Claude name; a kind with nothing named gets the
  three generic phrases and no Claude name; each single-slot-blank case reads no double space.
- `CommandModelsTests`: shipped claude is `Fable/Opus/Sonnet`; per-field fallback includes `coder`; the env row with
  `coder` is read; a row without it resolves to the shipped one; the shared json agrees with `Shipped`.
- `CommandTextsTests`: an override using `{coder}` is filled.
- `TheOrdersAreWhatTheyWereTests`: re-recorded; the diff is read and touches only model orders.
- Extension `commandModels.test.ts`: the shipped map matches `shared/command-models.json` with `coder`; `coder`
  round-trips through `commandModelsAfter` / `commandModelsFrom` / `commandModelsEnv`; the skew sentence names the coder
  slot against a 0.50.0 server and is silent against 0.51.0; `gateModelPickers`/panel harness see three pickers per kind.
- `helpCoverage.test.ts`: every language's gate article mentions *Code and search*.
- Whole suites: `CoaiMcp.Tests.exe` and `npm test` in `src_vs_code`; family checks `plan-lifecycle.mjs`, `pin-check.mjs`.

## 6. Definition of Done

- [ ] The Claude Code order states every row of §1's right column and opens with the bench's marker.
- [ ] Three slots, per-field fallback, generic words for an unnamed kind, no Claude model to another kind.
- [ ] An old server and a new extension, and the reverse, both work; the skew sentence covers the coder slot.
- [ ] The fixture re-recording touches only model orders.
- [ ] Help in five languages, `package.json` descriptions and schema, module docs and CHANGELOG updated.
- [ ] Both suites green; plan gate and code gate resolved; PR merged; mcp and extension released and their runs seen.
- [ ] This plan promoted to `research/` with its deviations.
