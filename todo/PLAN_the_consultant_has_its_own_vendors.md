# PLAN — the consultant has its own vendors

> Status: **plan only, nothing implemented yet, 2026-09-14.** Scope: the Consultant section of the
> panel (`consultantView.ts`, `consultSettings.ts`, `panelProvider.ts`, `settingsShape.ts`) and the
> server's consultant resolution (`ConsultationService.cs`, `ConsultantRouting.cs`,
> `SettingsJsonContext.cs`). Operator ruling, 2026-09-14.
>
> Related docs: [PLAN_consultant.md](../research/PLAN_consultant.md) — the feature as it shipped;
> [module_extension.md](../research/module_extension.md), [module_server.md](../research/module_server.md).
> Boundary with [PLAN_consultant_defaults_from_phase_0.md](PLAN_consultant_defaults_from_phase_0.md):
> that plan decides **which vendor** answers each caller by measurement; this one decides **where a
> consultant's configuration lives**. Neither changes the other's subject — the shipped default MAP
> (`claude→codex`, `codex→claude`, `gemini→codex`, `other→codex`) is phase 0's to move, and this plan
> keeps it byte-for-byte while changing what a map entry CONTAINS. This plan goes first; phase 0's
> numbers land on whatever shape is current.

## The symptom

A panel where nobody touched anything about consulting, on 2026-09-14:

```
Codex asks     [ claude — not configured any more ]
               [ the row's own model              ]
```

The consultant for a Codex caller is dead because a **reviewer** row was removed. Nothing about the
consultation changed; something next to it did.

The same seam shows in the picker beside it. `Claude Code asks` offers `codex · gpt-5.6-luna` — a
reviewer row's id and a reviewer row's model — while *Add a reviewer* two sections above offers
`Codex (OpenAI)`, `Antigravity (Google)`, `DeepSeek`. Two lists, one product, and the one a person
reaches for second is the one written in internal ids.

## The ruling

The operator, asked where a consultant's configuration should live (2026-09-14):

> я хочу что б список доступных физически был один и тот же. но настройки были независимые. у нас
> сейчас есть ревьюверы, консультант и чат. у них 3 один и тот же источник данных (кроме
> консультанта, ему тим сервер вобще запрещен) но настройки должны быть независимые (урл, модели)

In English: **one catalogue of what can be picked, three independent sets of settings.** Reviewers,
the consultant and the chat all draw the list of possible vendors from the same place; each keeps its
own URL, model and CLI path. Team servers stay forbidden for the consultant — that is already true
structurally (`remote` is absent from `CONSULTING_RUNTIMES`,
[consultSettings.ts:189](../src_vs_code/src/consultSettings.ts#L189)) and must stay true when the list
changes. The labels become the catalogue's own: `Codex (OpenAI)`, not `codex`.

**This is the move the chat already made, for the identical reason.** `ModelPreset` carries the whole
of what the chat needs to run — runtime, model, base URL, CLI path — and its type comment records the
operator asking five times ([chatPresets.ts:38-42](../src_vs_code/src/chatPresets.ts#L38-L42)): a
preset used to name a reviewer row and borrow its runtime, endpoint and path, *"so a reviewer switched
off took the chat with it and every picker read as a list of reviewers"*. The consultant is the last
feature still borrowing.

## What is true today

| Where | What it does |
|---|---|
| [consultantView.ts:96](../src_vs_code/src/consultantView.ts#L96) | The vendor `<select>` is built from `consultableVendors(state.vendors)` — the **reviewer rows** — and labelled `${one.id} · ${one.model}`. |
| [consultantView.ts:100](../src_vs_code/src/consultantView.ts#L100) | The model `<select>`'s default option means "the reviewer row's own model". |
| [consultSettings.ts:29](../src_vs_code/src/consultSettings.ts#L29) | `ConsultantChoice` stores `{ vendor: <reviewer row id>, model }`. Nothing else. |
| [consultSettings.ts:161](../src_vs_code/src/consultSettings.ts#L161) | `consultableVendors` filters reviewer rows: **switched off** is a refusal. |
| [settingsShape.ts:445](../src_vs_code/src/settingsShape.ts#L445) | `COAI_CONSULTANTS` ships the four `{vendor, model}` pairs; the definitions travel separately as `COAI_VENDORS`. |
| [ConsultationService.cs:253](../src_mcp/src/Server/Consultation/ConsultationService.cs#L253) | The server looks `choice.Vendor` up **in the reviewer catalogue** (`settings.Providers`) and refuses when it is absent or disabled. |
| [ConsultationService.cs:287](../src_mcp/src/Server/Consultation/ConsultationService.cs#L287) | An empty model falls back to `row.Model` — the reviewer's model. |

So a consultant is an id plus a borrowed row, and every failure mode above follows from that one fact.

## The shape

**A caller kind's consultant carries its own definition.** Four rows, four independent configurations:

```ts
/** What a caller's consultant IS — not the name of somebody's reviewer. */
export interface ConsultantChoice {
  readonly vendor: string;            // the preset id — names the vault entry, the logs, the refusals
  readonly runtime: Runtime;          // from the catalogue: codex | claude | antigravity | local
  readonly model: string;             // empty = the runtime's own default
  readonly baseUrl: string;           // for a vendor riding the codex CLI. Empty = the CLI's own
  readonly executablePath: string;    // empty = look it up on PATH
}
```

`vendor` stays because the **vault entry and the usage ledger are keyed by it**
([PanelSettings.cs:7](../src_mcp/src/Server/PanelSettings.cs#L7) — *"what names it in the panel, in
the logs, and in the vault entry"*). A consultant on `deepseek` therefore uses the `deepseek` key
already in the vault: a credential is not a setting, and asking for the same key twice under two names
is a second thing to rotate. URL, model and CLI path are settings, and those are the ones this plan
makes independent.

**No shared consultant list.** The alternative — a list of consultant entries, managed like chat
presets, that the four caller rows then pick from — was considered and rejected: four callers is the
whole population, a list of them would be a list nobody curates, and "which vendor answers Claude
Code" is already the question the row asks. Two callers wanting the same vendor each carry their own
copy, which is what "independent settings" means when it is taken literally.

**The picker is the catalogue.** `VENDOR_PRESETS` filtered by `CONSULTING_RUNTIMES`, with the preset's
own label and hint — the same filter-and-map the chat's step 1 already performs
([chatPresetsPanel.ts:333-340](../src_vs_code/src/chatPresetsPanel.ts#L333-L340)). Team servers are
never appended. A preset with an empty id (`Another OpenAI-compatible endpoint`) runs the same
name-and-base-URL input boxes `addVendor` runs
([panelProvider.ts:2325-2347](../src_vs_code/src/panelProvider.ts#L2325-L2347)), extracted so both
callers ask identically.

**The wire carries the definition.** `COAI_CONSULTANTS` grows the three fields; the server builds its
`ProviderSettings` from the entry instead of searching `settings.Providers`. This is a new wire field,
so it is measured against the **old server half** before it ships — a Team-server `remoteVendor` was
dropped silently by every component written before it existed, and the signature was a refusal nobody
could read.

## Growth surfaces

**None.** No table, collection, directory, cache or spawned process is added: the change moves four
existing settings keys from holding a reference to holding a definition, and the consultation records
(`ConsultationStore`) are untouched in shape, in number and in retention. The only value that grows is
the `coai.consultants` object in `settings.json`, by at most three short strings per caller kind, with
four caller kinds — bounded by `CALLER_KINDS`, which is a constant.

## Build order

1. **The widened `ConsultantChoice`** — the type, the reader over whatever `settings.json` holds, and
   the catalogue filter, in `consultSettings.ts` beside what is there. Pure, no `vscode`, unit-tested
   like its neighbours.
2. **The section renders from the catalogue** — vendor `<select>` of preset labels, model `<select>`
   from `modelsFor(runtime)`, and base-URL / CLI-path fields on the rows whose runtime takes them.
   `sameVendorNote` now takes the row's own runtime. `consultableVendors` loses its reviewer argument.
3. **The picker** — the shared preset-to-vendor flow, so the consultant asks for a name and a base URL
   exactly as *Add a reviewer* does.
4. **The wire and the server** — `COAI_CONSULTANTS` carries the definition; `ConsultationService` stops
   reading `settings.Providers`; the refusal sentences stop naming reviewer rows.
5. **Migration** — an existing `{vendor: 'codex'}` is a reference to a reviewer row. On first read it
   resolves against the catalogue by id (every shipped preset id matches), and against the reviewer row
   for anything custom, so a person who configured `mistral` keeps its base URL. Written back in the
   new shape the first time anything in the section is changed.
6. **Docs and help** — `research/module_extension.md`, `research/module_server.md`, and the five help
   catalogues (`helpContent`, `helpDe`, `helpEs`, `helpRu`, `helpUk`) in the **same commit** as the
   English, since a stale translation is invisible to `bodyFor`.

## Test plan

- **RED first, for the symptom**: a consultant configured for `codex`, the reviewer row deleted, the
  consultation still resolves. Today it refuses with `not configured any more`.
- A reviewer row switched off no longer changes what the consultant does — the second half of the same
  guarantee, asserted separately because it is a different branch of `consultableVendors`.
- The section offers catalogue labels; a `remote` preset or a configured Team server never appears in
  it; a runtime outside `CONSULTING_RUNTIMES` is not offered.
- Migration: an old map with a shipped id, a custom id matching a reviewer row, and an id matching
  nothing — three outcomes, none of them a thrown error.
- `envKeysMatchTheServer` / `panelServerDefaultsAgreement` extended to the new payload, so the two
  halves cannot disagree about its shape.
- C#: resolution from a definition, an unknown runtime refused **by name**, and a resumed consultation
  still pinned to the vendor it opened on.
- The page is exercised, not read: the new controls are asserted by RUNNING the section, per the
  standing prohibition on new source-text assertions (`.agents/PROJECT.md`).

## Definition of Done

- [ ] The Consultant section reads no reviewer row, in either half — `settings.Providers` is not
      consulted by `ConsultationService`, and `state.vendors` is not consulted by `consultantBody`.
- [ ] The vendor picker is `VENDOR_PRESETS` filtered by `CONSULTING_RUNTIMES`, with catalogue labels.
- [ ] No Team server can be chosen as a consultant, by construction rather than by filtering.
- [ ] URL, model and CLI path are per-caller and independent of Reviewers and of the chat.
- [ ] The vault entry stays keyed by the preset id; no credential is duplicated.
- [ ] Old `coai.consultants` maps keep working, including a custom vendor id.
- [ ] The new wire field was measured against a server half that predates it.
- [ ] Five help catalogues updated in the commit that changes the English.
- [ ] Red test observed before the fix, green after, whole suite run, both reported.
