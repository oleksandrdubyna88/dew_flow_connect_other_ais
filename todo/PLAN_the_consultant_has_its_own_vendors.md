# PLAN — the consultant has its own vendors

> Status: **EPICS A AND B IMPLEMENTED 2026-09-15 (A1 and A2 in PR #262; B3 and B4 on
> `feat/the-consultant-server-half`); epic C remains planned.** Written 2026-09-14 and reviewed by the
> gate the same day —
> verdict `good_enough`, 3 of 3 reviewers, 13 gating findings against a threshold of 6; twelve
> accepted and folded in below, two rejected with reasons (see *What the plan round changed*).
> Scope: the Consultant section of the panel (`consultantView.ts`, `consultSettings.ts`,
> `panelProvider.ts`, `panelView.ts`, `settingsShape.ts`) and the server's consultant resolution
> (`ConsultationService.cs`, `ConsultantRouting.cs`, `SettingsJsonContext.cs`). Operator ruling,
> 2026-09-14.
>
> Related docs: [PLAN_consultant.md](../research/PLAN_consultant.md) — the feature as it shipped;
> [module_extension.md](../research/module_extension.md), [module_server.md](../research/module_server.md).
> Boundary with [PLAN_consultant_defaults_from_phase_0.md](PLAN_consultant_defaults_from_phase_0.md):
> that plan decides **which vendor** answers each caller by measurement; this one decides **where a
> consultant's configuration lives**. Neither changes the other's subject — the shipped default MAP
> (`claude→codex`, `codex→claude`, `gemini→codex`, `other→codex`) is phase 0's to move, and this plan
> keeps it byte-for-byte, legacy-shaped, while changing what a map entry may CONTAIN. This plan goes
> first; phase 0's numbers land on whatever shape is current.

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
own URL, model and CLI path. Team servers stay forbidden for the consultant. The labels become the
catalogue's own: `Codex (OpenAI)`, not `codex`.

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

### Two facts found while verifying the shape — both correct the plan's first draft

1. **This build's server refuses a `codex` vendor that carries a base URL, by name.**
   [ConsultantResolution.cs:30](../src_mcp/runners/Consultation/ConsultantResolution.cs#L30) matches
   `"codex" when vendor.BaseUrl.Length == 0`; everything else falls through to `null`, and
   `CannotConsult` says so. `ConsultantsTests.ATeamServerRowAndACustomEndpointAreStillRefusedByName`
   pins it. So of the entries the catalogue offers, **DeepSeek, OpenRouter and any custom endpoint
   cannot hold a consultation in this build.** The first draft of this plan said a consultant on
   `deepseek` would use the `deepseek` vault key, which presumed otherwise. Teaching the codex
   consultant custom endpoints is a separate, measured change that file defers deliberately; this plan
   **renders the truth** about those entries instead, and keeps the id rule, because such an entry is
   storable whether or not today's server runs it.
2. **The server has no catalogue.** `VENDOR_PRESETS` is extension-only, so "resolve against the
   catalogue by id" cannot be one rule on both halves. What both halves *can* share is the rule
   `RuntimeResolution.NameOf` already applies: an id that names a consulting runtime IS that runtime.
   That is what the legacy rule below leans on; the catalogue is for LABELS and for what a NEW choice
   writes.

## The shape

**A caller kind's consultant carries its own definition.** Four rows, four independent configurations:

```ts
/** What a caller's consultant IS — not the name of somebody's reviewer. */
export interface ConsultantChoice {
  readonly vendor: string;             // the id — names the vault entry, the ledger, the refusals
  readonly runtime: Runtime | '';      // '' = a LEGACY reference, resolved as below
  readonly model: string;              // empty = the runtime's own default
  readonly baseUrl: string;            // for a vendor riding the codex CLI. Empty = the CLI's own
  readonly executablePath: string;     // empty = look it up on PATH
}
```

`vendor` stays because the **vault entry and the usage ledger are keyed by it**
([PanelSettings.cs:7](../src_mcp/src/Server/PanelSettings.cs#L7) — *"what names it in the panel, in
the logs, and in the vault entry"*). A credential is not a setting; URL, model and CLI path are, and
those are the ones this plan makes independent.

### The one resolution rule — stated once, used by four stories

A stored entry is either a **definition** (`runtime` non-empty) or a **legacy reference** (`runtime`
absent — everything written before this work, and the shipped defaults). A legacy reference resolves,
**identically on both halves**, in this order:

- **(a)** a reviewer row with that id, **enabled or disabled** → that row's runtime, model (unless the
  entry names one), baseUrl, executablePath. That is the semantics the entry always had, materialised;
- **(b)** else an id that is itself a consulting runtime (`codex`, `claude`, `antigravity`, `local`) →
  that runtime, the entry's model or empty, no baseUrl, no executablePath;
- **(c)** else **UNAVAILABLE**: the raw entry preserved, never rewritten, never defaulted; the server
  refuses it by name and the section says what to do.

`DEFAULT_CONSULT.byCaller` and `ConsultantRouting.Shipped` stay **byte-for-byte legacy pairs**. That
keeps the sibling plan's values untouched, keeps the panel↔server contract test unchanged, and —
because both halves resolve absence identically — fixes this plan's opening symptom (pristine
`codex→claude` with no `claude` reviewer row) **with no write at all**.

**Reading never writes.** Read-time resolution is the mechanism; the first edit in the section writes
the definition back.

**No shared consultant list.** A list of consultant entries managed like chat presets was considered
and rejected: four caller kinds is the whole population, a list of them would be a list nobody curates,
and "which vendor answers Claude Code" is already the question the row asks.

**The picker is the catalogue.** `VENDOR_PRESETS` filtered by `CONSULTING_RUNTIMES`, with the preset's
own label and hint — the same filter-and-map the chat's step 1 performs
([chatPresetsPanel.ts:333-340](../src_vs_code/src/chatPresetsPanel.ts#L333-L340)). Team servers are
never appended, and cannot be: they are not in `VENDOR_PRESETS`.

## What the plan round changed

Twelve findings accepted, folded into the shape above and the stories below:

| # | Finding | Where it landed |
|---|---|---|
| 1 | Migration must not wait for a UI write | read-time resolution (A1), wire projection (B4) |
| 2 | The server must refuse a runtime outside the consultant allowlist, **before** building a provider | B3 |
| 3 | Build order: migration reads reviewer rows that later steps take away | epic A is first |
| 4 | A legacy empty model meant *the row's* model; the new shape means *the runtime's default* | rule (a) materialises it (A1) |
| 5 | A custom endpoint needs a durable, unique, non-empty id | A2 refuses an empty id; C6 mints one |
| 6 | A stored id absent from the catalogue must stay selected, never silently substituted | C5 (stranded) |
| 7 | A legacy entry matching nothing needs an explicit unavailable state | rule (c), A1 + C5 |
| 8 | The server needs a dual-read fallback for a legacy payload | B3 |
| 9 | The new wire field must be measured against an OLD server half | B4 |
| 10 | The section must say these settings are the consultant's own | C5 |

Two rejected: that empty `baseUrl`/`executablePath` are ambiguous (they carry the shipped meaning of
`VENDOR_PRESETS` and `ProviderSettings`, and the picker's own validator closes the only reachable route
to an empty base URL), and that migration assumes every custom id is in the catalogue (rule (a) already
routes those to the reviewer row; the real gap is finding 7, which is accepted).

**Deviation this implies, stated so a code round sees it coming:** the first draft's DoD line
*"`settings.Providers` is not consulted by `ConsultationService`"* becomes *"…only on the legacy path"*.
Finding 8 requires that path to exist.

## Growth surfaces

**None.** No table, collection, directory, cache or spawned process is added. The consultation records
(`ConsultationStore`) are untouched in shape, number and retention. The only value that grows is the
`coai.consultants` object in `settings.json`, by at most three short strings per caller kind, with four
caller kinds — bounded by `CALLER_KINDS`, a constant.

## Build order — three epics, six stories

Each story ends complete: RED test observed failing for the real symptom → implementation → the suites
→ docs → its own `review_code` round with the scope → commit. Never a layer; always a reviewable slice.

### EPIC A — Migration first: a legacy consultant reads and writes as a definition

**A1 · The reader resolves a legacy consultant into a definition at read time** — *Fable*, because the
rule decides what every existing install consults on, silently, from the next read.
Files: `consultSettings.ts` (`ConsultantChoice`, `callers`, `sameCallers`, `isDefaultConsult`, new
`resolveConsultant`), `settingsShape.ts` (`envBlock`'s consultant block), `test/consultant.test.ts`,
`test/panelServerDefaultsAgreement.test.ts`, `research/module_extension.md`.
Done when: the type carries the three new fields and `''` means legacy; the reader trims what is stored
and never throws on junk; a pure `resolveConsultant(choice, vendors)` returns
`{kind:'definition',…} | {kind:'unavailable',…}` implementing (a)→(b)→(c); a **disabled** row still
resolves; `envBlock` keeps emitting exactly today's bytes through an explicit, commented projection
that B4 removes; `sameCallers` compares all five fields; a pristine map still writes no key.
RED: *a legacy entry reads as a definition — after the read its runtime is present*; and *a legacy
`codex` entry with an empty model materialises its reviewer row's model* (today `''`).
Discharges 1 (read half), 3, 4, 7 (state half).

**A2 · The write path stores a definition, never a bare reference** — *Opus*. Its own plan round
added four rules, each now pinned by a test: the caller's row is REPLACED by what the new vendor
resolves to, never merged into, so none of the old vendor's endpoint or CLI path is left behind; an
endpoint or CLI path typed at an UNPLACEABLE entry is refused rather than stored, because an entry
with no runtime has nothing for an endpoint to belong to; a definition resolves to ITSELF, so editing
one field never re-lends the reviewer row's values over a person's own; and the manifest's
`coai.consultants.default` — a third copy of the shipped map — is held level with the code.
Files: `settingsShape.ts` (`consultantRecordUpdate`), `panelProvider.ts` (`write`, `case 'caller'`),
`package.json` (`coai.consultants`), `test/consultant.test.ts`, `test/settingsAreDeclared.test.ts`.
Done when: an edit writes the edited caller's row as a FULL definition and leaves the other three rows
byte-identical; an empty or whitespace vendor id is refused and nothing is written; an unavailable entry
whose model is edited keeps its raw vendor and gains no invented runtime; the manifest declares the new
properties and stops saying "configured vendor row".
RED: *changing a model writes the caller's whole definition, not a bare reference*; *an empty vendor id
is never written*.
Discharges 5 (storage half), 4, 7 (write half).

### EPIC B — The server half, and the wire measured before it carries anything new

**B3 · The server resolves a definition, refuses a foreign runtime by name, and still reads a legacy
reference** — *Fable*, because it decides which CLI a working tree is sent to, and whether a Team
server can be reached at all.
Files: `SettingsJsonContext.cs` (`ConsultantDto`), `ConsultantRouting.cs` (`ConsultantChoice`, `Merge`;
`Shipped` untouched), `ConsultationService.cs` (`UnderTheLockAsync`, `NoSuchVendor`, `NewRecordAsync`),
`tests/ConsultScenarioTests.cs`, `ConsultantsTests.cs`, `ConsultSettingsTests.cs`,
`research/module_server.md`.
Done when: the DTO gains three nullable fields and `Merge` carries them trimmed; a choice with a
runtime is a definition — **if that runtime is not in `ConsultantResolution.Consulting` the call is
refused naming the caller kind, the vendor, the runtime and the allowlist, before any `ProviderSettings`
exists** — otherwise a `ProviderSettings` is built from the entry and `settings.Providers` is not
touched; a choice with no runtime takes the legacy path (a)→(b)→(c), where a **switched-off** row now
consults rather than refusing; a resumed consultation stays on the vendor, model and runtime frozen on
its record and takes `BaseUrl`/`ExecutablePath` from a current definition with that id, else the legacy
path, else the existing resuming refusal; the `codex`-with-base-URL refusal is unchanged.
RED: `AConsultantDefinedWithoutAReviewerRow_StillResolves`;
`ADefinitionOnTheRemoteRuntime_IsRefusedByName_BeforeAnyProviderIsBuilt`;
`ALegacyReferenceToASwitchedOffRow_ConsultsAnyway`;
`AResumedConsultation_StaysOnTheVendorItOpenedOn_WhenThePanelMoved`.
Discharges 2, 8, and the server halves of 4 and 7.
**Two deviations, decided while building it.** (i) A resumed consultation is refused when its vendor id
has since been redefined onto a DIFFERENT runtime — beyond the spec, which said only to take the
endpoint and CLI path from a current definition with that id. Borrowing them across a runtime change
would hand the codex adapter a Claude binary, which `vendor-routing.md` says is invisible in the
output; the refusal names both runtimes. (ii) **Closed by B3's own plan round, which both reviewers
raised independently.** The halves used to classify a runtime spelling differently — the panel read
`runtime: "Codex"` as a legacy reference and resolved it by id, while the server treated any non-empty
runtime as a definition and refused one outside the allowlist. One file, two answers. Both now match
WITHOUT case and answer in the allowlist's own spelling, so one name reaches the wire, `NameOf` and
every adapter. What remains asymmetric, deliberately, is a runtime name this build has never heard of:
the panel reads it as an older entry and resolves by id — how an extension meets a runtime a NEWER one
wrote — while the server refuses it, being the half that would launch it.

**B4 · The wire carries the definition — measured against the old server first** — *Fable*.
The measurement comes BEFORE the projection is removed: build the last released `mcp-v*` tag in a
throwaway worktree, drive it over stdio with a NEW-shape `COAI_CONSULTANTS`, and record the `consult`
reply verbatim for three cases — (i) a definition whose id has an enabled reviewer row with a different
model and endpoint, (ii) a definition whose id has no row, (iii) a definition with `runtime: "remote"`.
Prediction, recorded before the run: `System.Text.Json` ignores unknown members, so (i) runs on the
ROW's model and endpoint — the definition silently dropped, a skew that fails BACKWARDS; (ii) is a loud
"not configured" refusal; (iii) is refused by `CannotConsult`. Because (i) fails backwards, the panel
must say so while the installed server is older — the `ROLE_SWITCH_SINCE` pattern in `prompts.ts`.
Done when: `envBlock` emits the resolved definition for every non-default caller and the raw entry for
an unavailable one; a pristine map still emits no key; the three verbatim replies and the stated outcome
are in `module_server.md`; `CONSULTANT_DEFINITION_SINCE` and a pure `consultantSkewNote` exist and are
rendered; `panelServerDefaultsAgreement` asserts the DTO's property names equal the keys `envBlock`
writes, by reading the C#.
Discharges 1 (wire half), 9.
**What the measurement found, recorded so the prediction above is read as the prediction it was.**
Run 2026-09-15 against `mcp-v0.22.0` (4fe3cb02, the last release; `src_vs_code/scripts/measure-consultant-skew.mjs`,
replies verbatim in `module_server.md`). (ii) and (iii) as predicted. (i) was half wrong: the old
server dropped `runtime`, `baseUrl` and `executablePath` and consulted through the ROW — its CLI path,
launch for launch as the legacy pair — but with the DEFINITION's model, because `model` was on the wire
before B3; "the ROW's model" was never at stake. And the literal shape — a row carrying a different
model AND a base URL — does not "run on the row's endpoint": this build's codex consultant refuses a
custom endpoint (*two facts*, fact 1, above), so the old server REFUSES it, naming an endpoint the
person's consultant does not have. Both halves of (i) fail backwards, which the skew note says. Two
smaller deviations: (i) was measured in two cells on the codex route only (one that could run, one in
the literal shape), with a legacy-pair control beside them; and the wire is per CALLER — a caller at
its shipped pair stays off the wire rather than travelling as a legacy pair, since a resolved
definition would freeze this panel's reading of a caller nobody configured. The seam gained a fourth
leg — a consultant defined with no reviewer row answers through its own CLI path — watched failing
against the 0.22.0 build with the (ii) refusal. `CONSULTANT_DEFINITION_SINCE` is `0.23.0`, the next
`mcp-v*` release; whoever cuts it keeps the marker level with the tag.

### EPIC C — The section is the catalogue

**C5 · The section renders from the catalogue and reads no reviewer row** — *Opus*.
Files: `consultantView.ts` (whole), `consultSettings.ts` (`consultableVendors`, `sameVendorNote`),
`panelView.ts` (call site), `settingsShape.ts` (a catalogue id writes that preset's definition), the
five help catalogues in the same commit, `test/consultant.test.ts`, a new
`test/consultantSectionScript.test.ts` modelled on `panelPhrasesScript.test.ts`,
`research/module_extension.md`.
Done when: `consultableVendors()` takes no rows and offers the catalogue, naming what it refuses; a pure
row view-model is what the markup renders; each row has a vendor select of catalogue labels, a model
select from `modelsFor(resolved.runtime, …)`, and base-URL / CLI-path inputs where the runtime takes
them, every control carrying `data-caller`; a stored id absent from the catalogue stays selected and
labelled from its own fields; an unavailable entry is selected with an actionable sentence and no model
list; **a `codex` definition with a base URL carries a hint mirroring `CannotConsult` — this build does
not consult through a custom endpoint**; a sentence says these settings are the consultant's own;
`state.vendors` is gone from the section.
RED: *with no reviewer row at all, the catalogue is still offered*; and a run-the-page test that a
caller-keyed control posts its caller and the repaint restores the caret.
Discharges 6, 7 (UI half), 10.

**C6 · The custom endpoint asks for a name and a base URL exactly as *Add a reviewer* does** — *Opus*.
Files: `panelProvider.ts` (extract the name/base-URL boxes into one shared `askCustomEndpoint`, new
command), `panelView.ts` (`PANEL_COMMANDS`, the script's `save`), `consultantView.ts`,
`settingsShape.ts`, the two test files, `research/module_extension.md`, one sentence in each help
catalogue.
Done when: the entry appears in every caller's picker and choosing it posts a COMMAND carrying the
caller, never a setting write — the empty preset id is unstorable by construction; the host asks name
then base URL through ONE function both callers use; `id = normaliseId(name)`, refused when empty,
refused when another row holds that id with a DIFFERENT base URL, allowed when the URL matches — one
vault key, one credential; cancelling writes nothing.
RED (runs the page): *choosing the custom entry posts a command with the caller and writes no setting*
— today it posts a setting whose value is `__custom__`, an id nobody can use, about to be stored.
Discharges 5 (picker half).

### Order, and why

**A1 → A2 → B3 → B4 → C5 → C6.** A first: finding 3 — the read rule needs reviewer rows and C5 takes
them away. B before C, which reverses the first draft's numbering: if the section offered the catalogue
before the server could read a definition, a person would pick `deepseek` with no reviewer row, see it
selected, and the server would still refuse "not configured" — the opening symptom re-created for one
release. B3 before B4 so the server reads the field before anything sends it, and so B4's measurement of
the OLD server runs while the projection still stands.

## Test plan

- Extension: `npm test` in `src_vs_code` (baseline on this branch: **2678 passed, 0 failed, 1 skipped**).
- Server: the MTP executable `src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe`, never `dotnet test`
  (baseline: **1858 passed, 0 failed, 2 skipped**).
- Every story opens with its RED test, observed failing for the real symptom, reported with the pass.
- New section behaviour is asserted by RUNNING the page, never over its source text
  (`.agents/PROJECT.md`); the existing source-text assertions in `consultant.test.ts` are left to
  [PLAN_the_page_tests_run_the_page.md](PLAN_the_page_tests_run_the_page.md) and none is added.
- `panelServerDefaultsAgreement` keeps the two halves' defaults and DTO names level by reading the C#.

## Definition of Done

- [ ] The Consultant section reads no reviewer row for its PICKER, and `ConsultationService` reads
      `settings.Providers` only on the legacy path.
- [ ] The vendor picker is `VENDOR_PRESETS` filtered by `CONSULTING_RUNTIMES`, with catalogue labels.
- [ ] No Team server can be chosen as a consultant, by construction — and a wire payload naming
      `remote` is refused by name before any provider is built.
- [ ] URL, model and CLI path are per-caller and independent of Reviewers and of the chat.
- [ ] The vault entry stays keyed by the vendor id; no credential is duplicated.
- [ ] Old `coai.consultants` maps keep working with no write, including a custom id and an id that
      matches nothing — the last of which is an explicit, actionable unavailable state.
- [ ] The new wire field was measured against a server half that predates it, the three replies
      recorded verbatim, and the skew surfaced in the panel.
- [ ] The section tells the truth about entries this build's server cannot consult through.
- [ ] Five help catalogues updated in the commit that changes the English.
- [ ] Every story: RED observed before the fix, green after, both suites run whole, both reported, its
      own `review_code` round resolved, docs updated, committed.

## What is deliberately NOT in this plan

- The shipped default MAP values — a sibling plan owns them; the entries stay legacy-shaped.
- **Teaching the codex consultant custom endpoints.** `ConsultantResolution` defers it to a
  measurement; this plan renders the truth and does not change the refusal.
- Widening `ConsultationRecord` with `BaseUrl`/`ExecutablePath`. B3 resolves a resumed consultation's
  endpoint from the current definition; if B3's round finds that insufficient, freezing them on the
  record is a follow-up with its own store test.
- Team-server consultants, and any `providers` reporting of consultant definitions.
- A write-back of migrated entries on read — reading must not write.
- Retiring the existing source-text assertions in `consultant.test.ts`.
- **One shared, versioned capability contract for what a runtime can consult with** — raised by
  `codex` on C5's code round, and rejected for this plan rather than on its merits. Today
  `CONSULTING_RUNTIMES` is a documented MIRROR of `ConsultantResolution.Consulting`, and it has to be
  a mirror rather than something fetched: the panel must draw the picker on a machine where the
  server is not installed yet. The drift the finding names is real — teach the `codex` runtime custom
  endpoints on one half only and the two halves disagree about the same row — and is held today by an
  agreement test over the two lists plus B4's measurement across a version boundary. If that stops
  being enough, the shape to reach for is a contract the server SERVES and the extension falls back
  from, not a third copy of the list.
