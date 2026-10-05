# PLAN — One model catalog: the Settings page rebuilt around models you add once

> Status: **in progress, 2026-10-04 — E1 merged (PR #681); E2 story 1 built on `feat/catalog-e2`; E2.2–E5 open.** The design is accepted: the clickable mockup in
> [`new_design/`](../new_design/README.md) (open `new_design/index.html`; `node new_design/check.mjs` drives it, 61
> checks). Scope: the extension's Settings page (`src_vs_code/src`), the settings it writes and how they reach
> coai-mcp, coai-mcp's runners where the design adds a capability (`src_mcp`), the Team server's review request
> (`src_server`), help in five languages, and the docs. The sidebar and the Review rounds page are out of scope, with
> ONE stated exception: the sidebar's Bugz ranking picker reads the catalog (E5.1).
>
> Reviewed 2026-10-04 before any code: the coai plan round (session `652ef774`, 2 reviewers, `good_enough`, 15
> findings, all accepted) and two of the session's own reviewers (an architecture critic and a fact-checker). What
> they changed is in *What the review changed* at the end. Every `file:line` was read at `origin/main` = `e193822a`
> (extension 0.63.0, coai-mcp 0.43.0). Line numbers move; re-read before cutting.
>
> Related docs: [architecture.md](../research/architecture.md), [PLAN_settings_page.md](../research/PLAN_settings_page.md),
> [PLAN_the_consultant_has_its_own_vendors.md](../research/PLAN_the_consultant_has_its_own_vendors.md),
> [PLAN_the_security_tab_reads_at_a_glance.md](../research/PLAN_the_security_tab_reads_at_a_glance.md).

## The goal, as the operator asked it (2026-10-04)

1. **The tool works, but it carries too much, and models are chosen in too many places.** Some features pick a
   vendor and model of their own; others borrow a reviewer row. Rebuild the Settings UI/UX from scratch — keeping
   nothing for its own sake — so that it is convenient, clear and intuitive.
2. **A new "Available models" tab is the only place models are added or removed.**
3. **Every vendor and every model can be added many times.**
4. **Each added model carries checkboxes naming the features it may be used for**, and its own options — for
   example GLM 5.3 with *low* effort for reviews and a second GLM 5.3 with *high* effort for the consultant.
5. **Each model says whether it is reached through a CLI or an API key** — whether it can read the git checkout and
   the machine, which decides what the consultant can do with it.
6. **A system prompt per model** — Default selected, the prompt empty.
7. **Effort for every vendor except Antigravity** (whose effort is part of the model name).
8. **Two columns on a wide screen, one on a narrow one; blocks side by side are one height; every page keeps text
   size and brightness;** a control's "new" tag shows only in the first week after the update that brought it.
9. **Nothing the system does today may be lost** — the mockup's Design notes § 7 is the audited list.

## The operator's rulings (2026-10-04)

| Question | Ruling |
|---|---|
| One catalog for every feature? | **Yes** — reviews (plan, code, document, feature), security lane, consultant, question consultant, chat, Bugz ranking. |
| A different-vendor consultant | Ideal, **never blocked** — the page says when the caller and the consultant share a vendor. |
| A different effort for one model | **A second instance**, no per-feature override. |
| A key per instance | **Yes** — two instances may share a key or use two accounts. |
| Scope | **The Settings page only.** The sidebar and the Review rounds page are not touched (one stated exception above). |
| Tabs | **Six**: Models · Reviews · Consultants · Security lane · Chat · Setup; Reviews, Consultants and Setup have sub-tabs. |
| coai-mcp | Changes once the UI is accepted (it is). |
| Work order | Mockup → operator review → this plan → implementation. **The redesign first; the tails after.** |
| Security lane triggers | On each card under the prompt text: the signals as **two columns of checkboxes**, plus the card's **own words** (a word, a phrase, a piece of code, or a `/regex/`). |
| Review | The coai gate (back since 2026-10-04 evening), with the session's own reviewers alongside. |

## What is true today (verified at `e193822a`)

| Fact | Where | Consequence for this plan |
|---|---|---|
| A reviewer is a `Vendor` row: id, runtime, model, enabled, plan/code/document/feature flags (`document` is THREE-way: absent means "follows plan, but NO for a Team server"), baseUrl, remoteVendor, teamServerId, executablePath, prices, vaultKeyName, dialect, effort/thinking/reviewMinutes (api rows only). | `src_vs_code/src/vendors.ts:16-135`, three-way `:34-47` | **This row already IS an instance list** (`codex`, `codex-2`…). It is widened, not replaced (D1). |
| The rows reach coai-mcp as `COAI_VENDORS` inside `<dataDir>/settings.json`; process env outranks the file key by key. | `vendors.ts:640`; `settingsShape.ts:419`; `PanelSettings.cs:1139`; `SettingsFile.cs:57` | The wire stays these keys (D3) — no new key, so no new precedence rule. |
| **coai-mcp ignores JSON fields it does not know** — no `UnmappedMemberHandling` anywhere in `src_mcp` or `src_server`. | `src_mcp/src/Server/SettingsJsonContext.cs:30` (`VendorDto`) | A widened row is read by 0.43.0 as today's row: old servers keep working without a second wire. |
| The consultant is one definition per caller kind; a definition with `runtime: ''` is a *reference* to a reviewer row (resolution (a) row with that id, (b) a runtime of that name, (c) refused); an ABSENT caller means the shipped pair. | `consultSettings.ts:48-59`, `:139-144`; resolver `ConsultantResolver.cs:76` | Storage becomes references to rows; the wire carries resolved definitions (every server ≥ 0.23.0 reads them). "Unset → shipped" stays absent, so a pristine install still writes nothing. |
| Consulting runtimes: codex (no endpoint), claude, antigravity, local — in BOTH halves. | `consultSettings.ts:118`; `ConsultantResolution.cs:17`, codex-endpoint arm `:30` | A consultant on an API key or through the Codex CLI with an endpoint is NEW server work (E2.3). |
| Question-consultant rows have their own id, ONE prompt, enabled; at most 6 on; a row with no runtime borrows the reviewer row of that name. | `qconsultSettings.ts:30-41`, `:77`, `:92`; `QuestionAdmission.cs:134-135`; `QuestionResolution.cs:21` | Rows keep their id, prompt and switch and refer to an instance; several rows may refer to one instance. |
| Chat model presets carry their own runtime/model, `main` and `startingPrompt`; they are NOT per side and NOT sent to the server. | `chatPresets.ts:69-94`; `cliChatLaunch.ts:56`; absent from `settingsShape.ts:298-323` | Chat models become rows ticked Chat (per side, D8); the old preset key is frozen, never rewritten, so a downgrade still has it. |
| The Bugz ranking model is one string `rowId/model`, and both halves match the ROW ID `local`. | `bugzView.ts:46`; `panelView.ts:529-534`; `src_mcp/…/RankingModels.cs:41` | A second local instance is refused by 0.43.0 — fixed by runtime in E2.1, said as "ignored" before. The picker lives in the SIDEBAR (E5.1). |
| Every model list comes from one function; the runtimes include the retired `gemini`. | `models.ts:105`, `:35` | Reused unchanged; `gemini` rows still parse (they are migrated to antigravity as today, `vendors.ts:429-441`). |
| Per-side overlay stores whole values per key; data folder and side are ALWAYS per side; reads merge WORKSPACE values. | `settingsShape.ts:298`, `:342`, `:356-359`; `sideConfig.ts:30`, `:80` | **A cloned repository's `.vscode/settings.json` can set `coai.vendors` today** — and with system prompts, endpoints and regexes it would steer more. E1.1 reads model-bearing keys from the user layer and the side overlay only. |
| Effort exists only for API rows; local is ONE env value (`COAI_LOCAL_REASONING_EFFORT`) though `LocalRuntime` already passes a per-call value; Antigravity encodes effort in the model id; Claude and Codex pass none. | `ApiRowSettings.cs:23`; `PanelSettings.cs:354`, `:899`; `LocalRuntime.cs:152`; `AntigravityRuntime.cs:21-23`; `ClaudeRuntime.cs:63-94`; codex `ReviewerRuntime.cs:336-366` | Effort per instance is plumbing for local, new flags for codex and claude (E2.2), measured before any default changes. |
| The Team server's review request has no effort, system prompt or timeout; a v1 server SILENTLY ignores unknown fields and serves a client claiming a newer contract. | `src_server/src/ServerJsonContext.cs:91-98`; `ContractVersion.cs:41`, `:93` | Contract v2 (E2.5); the CLIENT reads `X-Coai-Contract` and says when a v1 server would drop a field. |
| Security signals are fixed word lists; a NonBacktracking regex with a 1 s timeout already exists for SQL shapes. | `SecuritySignals.cs:32-55` | Editable words and a card's own words are NEW (E2.4), on the same engine. |
| In Full code mode, API and local reviewers are told they hold a checkout they cannot read: the material is decided once per ROUND. | sentence `ReviewerPrompt.cs:89`; decision `RosterBuilder.cs:181-183` | Fixed per runtime in `RosterBuilder` (E2.1). |
| **The health probe of a second instance of a CLI vendor runs the ROW ID as the program.** Observed 2026-10-04: `providers` reports `claude-2` *"was not found — install it with npm …"* while the same row answered the plan round. | `VendorProbe.cs:143` → `IReviewerRuntime.DefaultExecutable => Provider` (`ReviewerRuntime.cs:226`); only Antigravity overrides it (`AntigravityRuntime.cs:126`); launches use the literal CLI (`ClaudeRuntime.cs:72`) | Every second instance looks broken in `providers`, the consultant health check and the Team server catalog. RED test first in E2.1: a `claude-2` row WITH `runtime: claude` probes `claude`. |
| The MCP server tab says settings apply "when your MCP client next starts it" — false since `PanelServiceHost` rebuilds on every settings-file change. | `panelView.ts:1864`; `PanelServiceHost.cs:5-23` | Tail T1; the new page says the true sentence. |
| Export/import is format version 1 with an EXACT check; it reads the base layer only and strips `executablePath`. | `configTransfer.ts:15`, `:27`, `:121`, `:191` | Format v2 with v1 migrated on import (E1.5). |
| An older extension refuses to overwrite a settings file stamped by a newer one. | `serverSettingsSync.ts:241-280` | A downgraded extension's edits never reach coai-mcp — said in POST_DEPLOY's downgrade path, tested. |
| The Settings page: 12 sections on one strip, the sidebar's whole document (`pageDocument` bundles the sidebar CSS and script with the CSP/nonce). | `panelView.ts:441-474`, `:566`, `:3244`; `panelSurface.ts:117`; `settingsPanel.ts:53` | The new page EXTRACTS the CSP/nonce part rather than reusing `pageDocument` whole (E3.1). |
| Version gates on the page. | `apiRuntime.ts:34` (0.37.0), `apiSettings.ts:26` (0.40.0), `featureGate.ts:28` (0.39.0), `consultSettings.ts:438` (0.23.0), `qconsultSettings.ts:92` (0.41.0), `securityLane.ts:26` (0.41.0), `prompts.ts:120` (0.18.10), `:131` (0.18.13), `rolesPage.ts:449` (0.19.0), `gateScope.ts:18` (0.33.0), `:41` (0.34.0), `commandModels.ts:185` (0.33.0) | One `skew(since, what)` road (E3.1). Each new capability gets its OWN constant (E2), not one "0.44.0". |
| Screenshots: `render-page.mjs` is Dark Modern only and knows the existing pages. | `src_vs_code/scripts/render-page.mjs:11-13`, `:125-157` | E3.1 adds the new page and a light theme. |
| Sizes: `panelView.ts` 3,398 lines, `panelProvider.ts` 4,297. | — | The new page is new modules; the old sections and pages are deleted in E5. |

## The design

The accepted mockup is the specification for every screen; this section fixes what the mockup cannot show.

### D1 — The catalog IS `coai.vendors`, widened

The reuse-first answer to the question "where do instances live": in the list that already holds them. A row gains:

| Field | Meaning | Absent means |
|---|---|---|
| `name` | The display name, the person's | the id |
| `uses` | The non-review features: `security`, `consultant`, `qconsult`, `chat`, `bugz` | none |
| `effort` | Every runtime but Antigravity, in the vendor's own spelling, validated against the shared levels (D4) | the vendor's or module's default |
| `systemPrompt` | ≤ 8 KiB | none |
| `timeoutMinutes` | Non-API rows (API rows keep `reviewMinutes`, whose absence is the module's calibrated value) | Limits › Reviewer timeout |
| `chatStartingPrompt` | What the composer opens with for this model | none |

The review features stay the existing flags — `plan`, `code`, `feature`, and the three-way `document` — so nothing
that reads a row today changes meaning. The page's "Use for" ticks are those flags plus `uses`. `vaultKeyName` stays
the key's name (absent = the id), so two instances share a key by naming it, Duplicate copies it, and removing an
instance never deletes a vault entry (coai never writes the vault).

**What crosses.** `vendorsEnv` writes a WHITELIST of fields (`vendorsWire.ts`, `vendorsEnv`), never the stored row, so none of the
new fields reaches `COAI_VENDORS` in E1. A row that reviews no stage (`plan`, `code` false, `document` off, no `feature`)
and whose `uses` do not include `security` is LEFT OUT of `COAI_VENDORS`: its consumers receive it resolved
(`COAI_CONSULTANTS`, `COAI_QCONSULT_ROWS`) or never cross at all (chat, Bugz). That keeps the env block of a migrated
setup byte-identical (plan-round finding 7, session `ac973900`). The prompt fields (`systemPrompt`,
`chatStartingPrompt`, both ≤ 8 KiB) never travel in an environment variable — 64 rows × 8 KiB is past Windows' 32,767-
character limit for one variable; E2.2 carries them through the settings file with a total-size budget refused by
validation (findings 4, 9).

### D2 — Everything else refers to a row by id

| Feature | Stored | On the wire (unchanged keys) |
|---|---|---|
| Consultant | per caller: a reference `{ vendor: <row id>, runtime: '' }`; ABSENT = the shipped pair | `COAI_CONSULTANTS` with the reference RESOLVED to a definition (every server ≥ 0.23.0), byte-for-byte what the stored definition wrote before |
| Question consultant | rows keep `id`, `prompt`, `enabled`; `vendor` = row id, `runtime: ''` | `COAI_QCONSULT_ROWS` resolved the same way (≥ 0.41.0) |
| Security lane | `runs[].vendor` = row id (already) | unchanged |
| Bugz | `bugzModel` = `<row id>/<model>` (already) | unchanged; the row-id-`local` match becomes a runtime match (E2.1) |
| Chat | `coai.chatModel` = row id (the key exists); the instances are rows with `uses: chat` | extension only |

### D3 — One write road, one migration, no dual store

- **Every write of a model-bearing setting goes through `sideConfig.saveSetting`** — including the OLD page's
  consultant, question-consultant and chat writes, switched in E1.4 to write rows and references. The two blocking
  findings (the old page writing keys the catalog never sees; a later catalog write silently reverting them)
  disappear because there is nothing to keep in sync: one list, one road.
- **Migration** (E1.3) runs once per settings LAYER (the user layer and each side overlay, each on its own), only
  where a definition is not yet a reference. Deterministic ids, never random: a consultant definition becomes row
  `consult-<caller>`, a question row's own definition `ask-<rowId>`, a chat preset `chat-<presetId>`, a Bugz model
  that differs from its row's model `bugz-<rowId>`. **Merge rule:** a definition joins an EXISTING row only when
  runtime, model, baseUrl, executablePath, dialect and vaultKeyName are all equal; otherwise it gets its own row. An id
  clash takes the next free `-N` (the existing `freeVendorId`, `vendors.ts:503`) and the fixture says so. The new row
  keeps the definition's key by setting `vaultKeyName` to the name it was stored under, so its vault entry and
  ledger name do not move. Running it twice changes nothing.
- **Migrated rows review nothing.** Every row the migration adds is written with `plan: false`, `code: false`,
  `document: false` and no `feature`, so neither this build nor an older one runs it in a round; the old page hides a
  row that reviews nothing and has a `uses` (finding 7).
- **Backup and restore:** before the first write, every key the migration writes in that layer — `vendors`,
  `consultants`, `qconsultRows` (absence recorded as absence; `chatModel` and `bugzModel` join when their sources
  move, E4.3 and E2.1) — is copied to that layer's own
  `migratedFrom` (the user layer's `coai.migratedFrom`; a side's overlay entry `migratedFrom`). Written ONCE, never
  overwritten by a later run. A command *ConnectOtherAIs: Restore settings from before the catalog* names the layers it
  will change, puts every key back exactly, and marks the layer `restored` so activation does not migrate it again until
  the person asks (findings 0, 1, 8). Kept until one release after the switch-over (T5).
- **Order and interruption:** backup → rows → references → the layer's marker `catalogMigration: "migrated"`
  (`"restored"` after a restore), written last. A restore writes in the reverse order — references, then rows, then the
  marker — so a stopped restore never strands a reference. A rerun finds an already-added row by its exact launch
  fields (the merge rule), preferring the deterministic id among the matches, so an interrupted run is
  finished, not duplicated. A layer that cannot be written (refused write, unreadable overlay) is skipped and reported,
  never blocks activation. A migration that would pass 64 rows writes nothing in that layer and says how many it needed
  (findings 2, 6, 11). Two windows activating at once both reach the same final state, because every write is a
  deterministic function of the backup.
- **A reference to a missing row** (deleted on the old page, or a half-run migration) keeps today's behaviour: both
  halves already name it unavailable, which says more than leaving it out would (finding 11; recorded as built in E1.3).
- **Workspace values.** The seven model-bearing keys (`securityLane` and `chatModelPresets` included) are read through ONE reader (user layer + overlay, via `inspect()`);
  a scan test refuses any other read of them. A workspace or folder value is ignored, and a one-time notice names the
  key and the file and offers to copy it to the user layer (findings 3, 10).
- **Downgrade, measured on the code:** the current build parses rows through `vendorsFrom` (`vendors.ts:332-376`),
  which keeps only typed fields, and writes the parsed list back (`panelProvider.ts:1859`), so an older build's first
  reviewer edit drops `name`, `uses`, `effort` (non-API), `systemPrompt`, `chatStartingPrompt`. Every feature still runs
  the same model: references are by id and the legacy (a) resolution ignores `enabled` (`consultSettings.ts:338-361`).
  On the next activation of this build, a row referenced by a consultant, question or chat setting regains that `uses`,
  and a `chat-*` row regains its starting prompt from the frozen `chatModelPresets`; names and system prompts are lost
  and POST_DEPLOY says so. No second store (finding 12).

### D4 — One shared file says what each runtime can do

`shared/feature-availability.json` — generated for TS, loaded by C#, both suites asserting against it (the
`runtime-capabilities.json` pattern) — holds: which runtimes serve which feature (replacing `CONSULTING_RUNTIMES`,
`Answering`, `CHAT_RUNTIMES`, `RANKING_VENDORS`), and the legal effort levels per runtime and API model. A write of an
effort the file does not list is refused naming the legal values.

### D5 — The new page is built behind a preview switch

`coai.settingsPreview` (user scope, never overlaid) makes *Open Settings* open the NEW page instead of the old one —
never both, so two webviews never write the same keys at once. The preview is offered only from E3 (Models) on.

### D6 — Effort defaults stay empty

The vendor's or module's own default, for every runtime that gains effort, until a measurement of ≥ 3 runs per vendor
on the product path says otherwise (`RESULTS_model_comparison.md`: agy Flash Low beat High; a local model with thinking
on never answered). Each runtime's flag is pinned in argv AND observed taking effect once, recorded beside the code.

### D7–D11

| # | Decision | Why |
|---|---|---|
| D7 | **Bugz has one ranking model** — ticking it moves it. | Today's setting is one model. |
| D8 | **Chat models become per side**; chat PROMPT presets stay shared. A model's effort and system prompt apply to chat too (chat runs in the extension). | The catalog is per side; a chat on WSL opens a WSL CLI. |
| D9 | **A consultant on an API key** is a multi-turn API runner; coai-mcp keeps its transcript (the vendor keeps none), retired with the consultation. | The operator's own example (GLM high as consultant). |
| D10 | **✓ Check of any instance** is a coai-mcp one-shot mode `--check-model <id>`, built on `ConsultCheckState`'s durable record (exclusive open, owner = pid + process start time, `already-checking`, a startup sweep, never settled from another side, the config hash stamped so an edit mid-check is not credited), one at a time per instance, a hard timeout that kills the process tree, the scratch folder removed in `finally`, confirmed first as a paid turn. | The consultant's Check proved the shape; generalising it is reuse, not a second mechanism. |
| D11 | **Help moves with the tabs**, in all five languages, in the epic that moves a control; `coai.openSettings` maps every OLD tab id to its new place. | A help article naming a tab that no longer exists is the stale translation already measured. |

### What the legacy wire cannot carry, and what the page says

| Catalog fact | coai-mcp 0.43.0 sees | The page says (until 0.44.0 is installed) |
|---|---|---|
| Effort on a CLI row, a system prompt, a non-API timeout | the row without them | "this coai-mcp ignores: its effort, its system prompt, its own time limit" on the card |
| A consultant on an API key or through the Codex CLI with an endpoint | a definition it refuses by name | on the Consultants tab, "needs coai-mcp 0.44.0" |
| A Bugz model on a second local instance | refused (row id ≠ `local`) | "needs coai-mcp 0.44.0" on the card |
| Editable signal words, a card's own words | the shipped words only | on the routing table and the card |
| A model used only for the consultant, question consultant, chat or Bugz | nothing — the row is left out of `COAI_VENDORS`; its consumer receives it resolved | nothing; it is correct |

### Growth surfaces

| Surface | Projected size | Who retires it | Interrupted |
|---|---|---|---|
| Rows in `coai.vendors` | ≤ 64 (refused past it), each ≤ 1 KB + a system prompt ≤ 8 KiB → realistic < 20 KB, worst ~0.6 MB of `settings.json` | the person | written whole by `saveSetting`; a refused write snaps back |
| Migration backup `coai.migratedFrom` | one copy per layer, a few KB | T5 | the migration is idempotent; the backup is written BEFORE the rewrite |
| First-seen version per "new" control (`globalState`) | one entry per release with new controls, ~10 bytes | pruned after 60 days on activation | — |
| ✓ Check records, transcripts, scratch folders | one record + one transcript per instance, overwritten; a scratch folder only while a check runs | removed with the instance; scratch removed in `finally` and swept at startup | D10's sweep ends a dead owner's record as `interrupted` |
| API-consultant transcripts | ~2–20 KB per turn × ≤ 5 turns × ≤ 10 consultations per session ≈ ≤ 1 MB per session | deleted when the consultation is closed or idle-closed; anything older than 14 days swept on start | the existing consultation sweep marks it interrupted |
| `new_design/` and its Sonar exclusion | ~6,000 lines, never shipped | E5.3 | — |

## Build order — five epics, one pull request and one gate round each

Each epic ships on its own; the old page works until E5. Every coai-mcp/Team-server change is read behind its own
version gate in both directions.

### Epic 1 — The instance list grows into the catalog (extension + shared; nothing visible changes)
1. **Widen the row** (D1): the new fields, parsing with "absent means the shipped value", validation (> 64 rows,
   prompt > 8 KiB, unknown effort — each refused naming the legal values), and model-bearing keys read from the user
   layer and the side overlay only, never a workspace or folder value. RED test: a workspace `.vscode/settings.json`
   carrying `coai.vendors` changes nothing.
2. **`shared/feature-availability.json`** (D4) with its TS generator and C# loader; the four runtime lists in the
   extension replaced by it (the C# side follows in E2.1). **Frozen to today's lists in E1**: until E2.1 reads the file,
   coai-mcp keeps its own copies, so the existing TS-against-C# mirror tests stay and compare the file to them
   (finding 13).
3. **The per-layer migration** (D3): deterministic ids, the merge rule, the clash rule, backup once, the marker last,
   the restore command, the 64-row check, an unwritable layer skipped. Fixtures: today's shapes from the repo's
   settings fixtures and the operator's own settings (anonymised) — overlapping reviewer, consultant, question and chat
   entries, a forked side whose overlay predates migration, a `gemini` row, a codex row with an endpoint; migrate twice
   = once; a run stopped after each write and rerun reaches the same state; restore → reload → still restored;
   downgrade (fields stripped as `vendorsFrom` strips them) → rerun → nothing duplicated, `uses` regained.
   **As built (2026-10-04), two sources moved to the epics that change their readers:** chat presets go to E4.3 —
   saved conversations refer to a preset by id (`chatConfig.ts:237`, `chatHooks.ts:229`), so a row `chat-<id>` would
   move a resumed conversation onto another model; the Bugz model goes to E2.1 — ranking accepts only the row id
   `local` on both halves (`RANKING_VENDORS`, `RankingModels`), so a `bugz-local` row would be refused until the
   runtime match lands. E1.3 moves consultant and question-consultant definitions. A consultant never becomes a
   reference equal to its caller's shipped pair (that caller would drop off the wire). A reference to a missing row
   keeps today's behaviour — both halves already name it unavailable — rather than being dropped from the env block.
4. **One write road**: the old page's consultant, question-consultant and chat writes switched to rows + references
   through `saveSetting`; the env block resolves references to definitions. RED test per surface: an edit on the old
   page after migration changes the env block coai-mcp reads. The env block for an unchanged, migrated setup is
   byte-identical to today's (the three-way `document` kept, pristine still writes nothing), measured on the fixture
   WITH overlapping consultant, question, chat and Bugz entries, not a reviewer-only one; the seam test compares
   0.43.0's `--providers` reviewer set before and after migration.
   **As built (2026-10-04):** the old page keeps writing definitions (its pickers resolve the reference first), and
   `catalogEdit.foldedWrite` in `PanelProvider.save` folds each write into the catalog before it lands — an entry
   that referred to a row only it uses rewrites that row in place, so an edit never forks or orphans one; a shared
   row is never rewritten. The old page hides a catalog-only row (`shownOnTheOldPage`), and its three reviewer-list
   writes that went around the side overlay now go through `save`. The seam's tenth leg proves the byte-identical
   settings file and the unchanged reviewer set against the real binary.
5. **Export/import v2** (rows + references; `executablePath` stripped as today); a v1 file is migrated on import and
   REPLACES the current models only after a confirmation that names the row count and every row whose vault key this
   machine does not hold; the current models are backed up first and the restore command puts them back (finding 5).
   **As built (2026-10-04):** the backup is an export of the current setup to `<dataDir>/config-backups/` (newest ten
   kept), named in the confirmation and put back by *Import config* — the restore command keeps one meaning, "before
   the catalog", rather than two. Vault key names come from the panel's last `--providers` answer and are said as
   not known yet when nobody has asked. A v1 file's definitions are moved into rows by the migration that runs on
   the configuration change the import makes.

### Epic 2 — coai-mcp and the Team server honour the new fields (release mcp 0.44.0; Team server contract v2)
1. **Probe and routing fixes**: the probe runs the runtime's CLI, not the row id (RED: `claude-2` with
   `runtime: claude`); Bugz ranking matches the runtime, not the id `local`; an unknown runtime refused by name; the
   material told to each reviewer decided per runtime in `RosterBuilder.cs:181-183`; the C# side reads
   `feature-availability.json`. **Carries the Bugz move from E1.3**: once both halves match the runtime, a Bugz model
   that differs from its row's model becomes row `bugz-<rowId>` (`uses: [bugz]`) by the same pure migration.
2. **Per-row system prompt, timeout and effort in every runner** (review, consult, question consult): the prompt
   delivered inside the prompt body (stdin or the prompt file the runner already uses), NEVER argv, never logged
   (length and hash only — a test asserts no launch record contains it); refused by name past a runtime's limit.
   Effort: codex `-c model_reasoning_effort=…`; claude's flag verified against the installed CLI first; local per call
   (`LocalRuntime.cs:152` already takes it); levels from D4. Each pinned in argv/body AND observed once. Gates
   `SYSTEM_PROMPT_SINCE`, `TIMEOUT_SINCE`, `CLI_EFFORT_SINCE`.
3. **A consultant on an API key and through the Codex CLI with an endpoint** (D9): the shared list replaces
   `ConsultantResolution.Consulting`; transcripts kept and retired as in *Growth surfaces*. Gate `API_CONSULTANT_SINCE`.
4. **Security words, a card's own words, and two one-shot modes**: words and patterns read from the lane setting
   (shipped words when absent); patterns run on `RegexOptions.NonBacktracking` with a match timeout, ≤ 32 patterns of
   ≤ 200 characters, a refused pattern reported by name. RED tests with catastrophic patterns over a 1 MB diff: the
   round completes and the pattern is reported. `--check-security <text>` (what "Try it" calls) and `--check-model <id>`
   (D10) — **both added to the one-shot list in `.agents/PROJECT.md`**, never exit 64. Gate `SECURITY_WORDS_SINCE`.
5. **Team server contract v2**: `ReviewRequestDto` gains `Effort`, `SystemPrompt` and `TimeoutSeconds` is honoured per
   model; a client system prompt is accepted only when the operator's switch allows it (off by default), capped, never
   logged; the operator can cap effort. The client reads `X-Coai-Contract` and, against a v1 server, says which fields
   that server drops. Measured both ways. Deploy is manual and needs the operator's go-ahead.

**As revised by epic 2's plan round (coai session `589a145b`, 2026-10-04, good_enough, 19 findings accepted)** — these
override the stories above where they differ:

- **Capability, not version numbers.** coai-mcp gains a one-shot `--features` that lists the row fields and modes it
  accepts (`systemPrompt`, `timeoutMinutes`, `cliEffort`, `apiConsultant`, `securityWords`, `checkModel`,
  `checkSecurity`); the extension writes a field only when the installed binary lists it, and says on the card which
  fields this binary ignores. A seam leg per field fails when the extension would send a field the built binary does
  not list. No `*_SINCE` constant guesses the release number before release-please cuts it. (f1, f8)
- **Effort.** A codex effort stays in the settings but is NOT sent while codex is `unmeasured`, and the card says
  "not applied"; an E1-shaped settings file with a codex effort still yields a round with codex in it. The probe
  records whether `claude --help` lists `--effort`; a claude row with an effort on a CLI without it is refused by name
  for that reviewer only ("the installed claude CLI does not take --effort"), never the whole round. A refused field
  degrades one reviewer, reported, never aborts a round. (f5, f10, f11)
- **Sent is not applied.** Per runtime, the measurement that shows the effect is named and recorded beside the code: a
  system prompt that forces a marker token in the answer (one recorded real call per runtime), the effort level as
  the CLI/API reports it in its usage or metadata where it does; where no effect can be observed, the docs and the card
  say "sent", not "applied". (f12)
- **The system prompt's leak paths.** Delivered in the prompt body after the product's own reviewer instruction (which
  it cannot replace: the output schema and read-only rules come after it again), never argv. A canary test runs a round
  through success, timeout, CLI failure and API error and greps stderr, the log files, the rounds database and the
  run records; a prompt file is deleted in `finally`. Only length and hash are recorded. (f0, f2)
- **Team server.** The operator caps timeout and effort (clamped, said in the response); a client system prompt is
  appended after the server's immutable instruction, recorded as client-supplied (hash only), stored nowhere. With the
  operator switch off (the default) the server accepts the request, drops the field, and reports the drop in the
  response — shown by the client exactly as a v1 server's drops are. A dropped field is a visible per-round note in the
  round log, never silence. (f1, f9, f13)
- **Security words and patterns.** Each pattern is compiled once when the setting is validated (the extension asks
  `--check-security --validate` through stdin), per pattern, so a construct NonBacktracking refuses (lookaround,
  backreference) or a slow construction is refused by name at save time; a pattern refused at round time marks the
  lane DEGRADED in the verdict rather than quietly scanning with fewer signals; a total scan budget bounds 32 patterns
  × a 1 MB diff. `--check-security` reads its text and the patterns being edited from stdin (never argv: no token on
  a command line, no 32 K limit); a bad argument exits 65, never 64; the extension falls back on 64 (an older binary).
  (f4, f6, f14, f17)
- **API-consultant transcripts — growth.** Under `<dataDir>/consultations/<id>/transcript.jsonl`, owner-only mode; ≤ 1 MB
  per consultation — the next turn past it is REFUSED by name, never truncated; total ≤ 50 MB, a new consultation
  refused when full; swept (closed or idle consultations, and anything older than 14 days) on every coai-mcp start and
  by `--close-consult`; an interrupted turn is ended `interrupted` by the existing consultation sweep. (f3, f15)
- **Bugz migration** reuses `catalogMigration.migrateLayer` (backup once, deterministic id, clash `-N`, marker last,
  idempotent, restore); history keys are untouched. (f7, f16)
- **`--check-model` widens `--check-consultant`** rather than standing beside it: one `ConsultCheckState`-based check
  for any row. (f18)
- **Scope.** Five stories, two release lines and a manual deploy: built as one epic per the operator's rule, released
  as mcp 0.44.0 with whatever stories have landed — the `--features` list, not a version, tells the extension which.

#### E2.1 as built (branch `feat/catalog-e2`)

- **Probe.** `ClaudeRuntime`, `CodexRuntime`, `GeminiRuntime` name their CLI in `DefaultExecutable`; launch and probe
  both use it, so a `claude-2` row probes `claude` (`ARowIdIsNotAProgramTests`).
- **Material per reviewer.** `IReviewerRuntime.ReadsTheCheckout` (false for api, local, remote) and
  `ReviewerMaterial.For` decide per reviewer in `RosterBuilder`; only a reviewer that can read a checkout is told it has
  one (`AReviewerIsToldOnlyWhatItCanReadTests`). `CodeWorkspaceTests`' repair-launch test had pinned the old lie on a
  local reviewer; it now asserts the guarantee on a CLI reviewer and the absence on a local one.
- **The shared file.** `core/Catalog/FeatureAvailability.cs` embeds `shared/feature-availability.json`
  (`FeatureAvailabilitySeed` in `CoreJsonContext`), refuses a feature list that names no runtime or an undeclared one,
  and `ConsultantResolution.Consulting` IS `FeatureAvailability.Builtin.Consultant` — RED first on "equal but a copy".
- **Unknown runtime — a reversed decision.** Both halves turned an unknown runtime into `codex` ("a name from a newer
  panel still launches something"); what it launched was the Codex CLI on the person's own account, the shape `api`
  was once the example of. The server now KEEPS the name (`PanelSettings.RuntimeOf`), `RuntimeResolution.NameOf`
  answers it before the base-URL arm, `For` no longer falls back to the row id when a runtime was named (a row `claude`
  with runtime `llama.cpp` ran the claude CLI), and the probe and the round's exclusion both say
  `RuntimeResolution.NoAdapterFor` — the runtime by name and the runtimes this build runs. Two tests that pinned the
  old coercion were rewritten. **Not done here:** the extension's `vendorsFrom` still coerces an unknown runtime to
  `codex` when an OLDER panel reads a newer one's rows (and would re-save it so); that is an extension change, tail T6.
- **Bugz ranking by runtime.** `RankingModels.IsAllowed(model, runtime)` / `Refusal(model, runtime)`; `--collect-bugs`
  takes `--runtime` (the catalog row's runtime, which the extension resolves — Bugz never crosses in `COAI_VENDORS`, so
  the collector cannot look it up). Without it the row id stands in, so `local/<model>` from a terminal or an older
  extension means what it did. A row CALLED `local` on a cloud runtime is now refused. The extension passes
  `--runtime` once `--features` lists it (E2.2), and the Bugz migration (`bugz-<rowId>`) follows that.

#### E2.2 as built so far

- **`--features`** (`Server/FeaturesMode.cs`): `{"features":[...]}`, an entry added in the commit that makes it true;
  today `bugzRuntime`. The extension reads it once per binary file (`binaryFeatures.ts`; 64, a failure or a bad answer
  is no features) and seam leg 11 fails when the extension knows a capability the build does not list.
- **Bugz by runtime, end to end**: the picker offers every `local`-runtime instance when the binary ranks by runtime,
  and `--collect-bugs` gets `--runtime` only then (`bugzView.collectArgs`).
- **The Bugz migration** (the E1.3 carry): `migrateLayer(layer, { bugzByRuntime })` moves a Bugz model that differs
  from its row's into `bugz-<rowId>` — only for a binary that ranks by runtime. Deviation: Bugz's own row is rewritten
  IN PLACE on a later pick and a pick through it moves nothing (the picker writes on every pick; the plain rule left an
  orphan per pick). The backup gains `bugzModel` only when it is moved, added to an epic-1 backup, never overwriting.
- **A prompt file does not outlive its launch**: `ReviewerInvocation.TempFiles`, deleted by the scheduler in a `finally`.
- **The system prompt**: in the body after the product's instruction and before the contract; never argv; refused past
  8192 bytes by name; redacted from the child's output before anything is recorded (the canary found the leak: a
  failing CLI's stderr became the round's reason). Deviation: NOT sent in a Team server row's body — story 5's field.
  The extension sends it only when `--features` lists `systemPrompt`. "Sent is not applied" still owes its one recorded
  real call per runtime (a marker the answer must carry).
- **A CLI row's timeout** (`timeoutMinutes`, 1–1440): its launch timeout in place of the round's; an api row keeps
  `reviewMinutes`. Listed in `--features`; the extension sends it only then.
- **A CLI row's effort** (`cliEffort`): claude's level as `--effort` (verified on claude 2.1.289), a local row's per call,
  a codex row's kept and not sent (unmeasured); a level a runtime does not take leaves that reviewer out by name.
  Deviation: no `--help` probe per launch — an older claude refuses the flag itself, and that failure is named
  ("the installed claude CLI does not take --effort") for that reviewer only.
- Still open in E2.2: "sent is not applied" — one recorded real call per runtime showing the system prompt's effect
  (a marker the answer must carry) and the effort as the CLI reports it; until then the docs and card say "sent".

### Epic 3 — The new Settings page: the shell and Models (behind the preview switch)
1. **The shell**: a page module of its own (pure page + thin host); the CSP/nonce extracted from `pageDocument` and
   shared; `tabStrip` + `tabKeys`, `selectSearch`, the busy marks, focus restore and the refused-write snap-back reused;
   six tabs with sub-tabs, remembered by the host; the deep-link map (D11); two columns from 1100 px with
   side-by-side blocks one height (CSS subgrid); `help(key)`, `skew(since, what)`, `newTag(controlId)` and one confirm
   dialog; `coai.settingsPreview` (D5); `render-page.mjs` gains the page and a light theme.
2. **Models — cards and editing**: every card part of the mockup; add (grouped by where a model runs), duplicate
   (copies `vaultKeyName`), remove (lists every reference; the last switched-on plan or code model cannot leave), on/off,
   the filter rows.
3. **Models — the world-facing parts**: the CLI's ▶ open / ⤓ install / ⟳ update, coai-mcp's verdict, where a list came
   from and "ask again", the API "runs it at", the off-machine endpoint warning and the WSL fix, ✓ Check (D10), the
   per-card "this coai-mcp ignores…" note.

### Epic 4 — The feature tabs use the catalog
1. **Reviews**: Stages; Roles & prompts (replacing `rolesPage.ts`) with ONE switch per role — `roleEnabled` and the
   catalog's `active` merged, `COAI_ROLES` still written for servers under `ROLE_SWITCH_SINCE`; deletion's confirmation
   and reserved ids; a role off until it has a question; Prompts per round; The gate; Commands (replacing
   `commandsPage.ts`); Limits.
2. **Consultants**: callers refer to instances ticked Consultant (absent = shipped pair; same vendor shown, never
   refused; stranded picks shown); health per side; the paid Check confirmed; agy's allow rule; the question
   consultant's rows, prompts, folders and limits.
3. **Security lane and Chat**: pairs from instances ticked Security lane; on each card the signals in two columns, own
   words, prompt text in place; the routing table with editable words; Try it through `--check-security` (never a
   JavaScript copy of the matcher). Chat from instances ticked Chat, per side, the starting text and the model's effort
   and system prompt applied; prompt presets inline (replacing `chatPresetsPage.ts`). **Carries the chat move from
   E1.3**: each model preset becomes a row with `uses: [chat]` and its `chatStartingPrompt`, and every stored
   conversation's preset id is mapped to that row in the same change, so a resumed conversation keeps its model.
4. **Setup**: keys counted across every row; the CLI table; Team servers with every state; the MCP server's states and
   clients (another program's config is READ only, never written, and no secret from it is shown); the data folder's
   Change and Move flows on today's `dataCommands.ts` logic, the "moved from" record surviving a reload; This side with
   the export note.

### Epic 5 — The switch-over, docs and release
1. **The new page is Settings**: the preview switch removed; the twelve old section builders, their commands and the
   three replaced pages (`rolesPage.ts`, `commandsPage.ts`, `chatPresetsPage.ts` and their panels) deleted; the
   sidebar's Bugz picker reads the rows ticked Bugz (the one sidebar change).
2. **Help and docs**: help in five languages (≈ 70 `HELP` keys and the articles that name tabs — written by the
   implementer in all five, as today); `research/module_extension.md`, `research/module_server.md`, `architecture.md`,
   CHANGELOG; POST_DEPLOY gains "a migrated install opens on Models with every old reviewer, consultant and chat model"
   and the downgrade path.
3. **Clean-up**: `new_design/` and its Sonar exclusion deleted; the restore command kept one more release (T5).
4. **Releases**: mcp 0.44.0 first (E2), the Team server deploy when the operator says, then the extension; post-deploy
   checks against the installed builds.

## Test plan

- **Every story: RED first**, then GREEN; `npm test`, `test:contract`, `test:seam`, `test:host`, the C# suites as MTP
  executables — never `dotnet test`. Before a release, ALL suites.
- **Migration and the write road (E1):** the fixtures above; idempotence; the byte-identical env block; an old-page edit
  reaching the env block; a two-side test (a change on side A leaves side B's env block alone); the workspace-value
  refusal.
- **Skew, every direction:**
  - new extension + coai-mcp 0.43.0 with a catalog it cannot fully express (two CLI instances, multi-use rows,
    effort): parsed by the REAL 0.43.0 binary (`--providers` over the written file, in `test:seam`), and the page's
    "ignored" notes;
  - coai-mcp 0.44.0 + the old extension;
  - an old extension against a file stamped by a newer build;
  - a forked side whose overlay predates the migration;
  - a process-env `COAI_VENDORS` beside the file's;
  - coai-mcp 0.44.0 against a Team server v1 with a non-empty system prompt;
  - a v1 export imported after migration.
- **Runners (E2):** each runtime's argv/body pinned with effort, system prompt and timeout; no launch record holds a
  system prompt; catastrophic regexes; both one-shot modes' exit codes.
- **Pages (E3–E4): tested by RUNNING them** (`bundledPage.test.ts` and the page harnesses), never by asserting page
  source text; every one of the mockup's 61 `check.mjs` flows has a counterpart page test.
- **Layout:** `render-page.mjs` screenshots of every tab at 1920 and 900 px in both themes, read before each PR and
  attached to it; equal heights asserted structurally.

## Boundaries with other plans

| Item | This plan | The other plan's part |
|---|---|---|
| Splitting `panelProvider.ts` | Deletes the settings handlers in E5 instead of moving them | [PLAN_the_panel_provider_is_too_big.md](PLAN_the_panel_provider_is_too_big.md) keeps its sidebar clusters; its vendor, Team-server, local-engine and price clusters are superseded — **this plan goes first** |
| Splitting `panelView.ts` / `roundsLog.ts` | Deletes the settings half of `panelView.ts` in E5 | [PLAN_two_files_outgrew_the_rule.md](PLAN_two_files_outgrew_the_rule.md) keeps the sidebar half and `roundsLog.ts` |
| Splitting `PanelSettings.cs` | E2 adds fields to `VendorDto` and its parsing | [PLAN_panel_settings_is_too_big.md](PLAN_panel_settings_is_too_big.md) moves the parsing — whichever lands second rebases onto the other; E2 adds no new section to the file |
| Local effort in the panel | E2/E3 expose it per instance | [PLAN_local_trust_and_vllm.md](PLAN_local_trust_and_vllm.md) keeps per-origin acknowledgement, vLLM keys, 401 reading, `num_ctx` refusal |
| A model's liveness | E2.1 fixes the probe's program; E3 draws the verdict and ✓ Check | [PLAN_provider_liveness.md](PLAN_provider_liveness.md) owns the three liveness states and their cache |
| How a probe says it is working | E3 uses it | [PLAN_panel_probing_state.md](PLAN_panel_probing_state.md) owns it |
| Arrow keys on tab strips | E3.1 consumes `tabKeys` | [PLAN_the_tabs_announce_themselves.md](PLAN_the_tabs_announce_themselves.md) steps 2–4 |
| The chat presets page writing on every keystroke | E4.3 replaces the page | [PLAN_chat_presets_write_every_keystroke.md](PLAN_chat_presets_write_every_keystroke.md) is superseded once E4.3 lands |
| Which vendor answers which caller by default | E4.2 keeps the shipped map (absent = shipped) | [PLAN_consultant_defaults_from_phase_0.md](PLAN_consultant_defaults_from_phase_0.md) owns the measurement |
| Checking a WSL consultant from Windows | D10 generalises the check record | [PLAN_a_wsl_consultant_is_checked_from_windows.md](PLAN_a_wsl_consultant_is_checked_from_windows.md) keeps the cross-side launch |
| The question consultant's API and web tails | E2.3 adds the API consultant; D4 the shared file | [PLAN_question_consultant_tails.md](PLAN_question_consultant_tails.md) keeps agy web and api web |
| Translations that go stale | D11 moves help with the tabs | [PLAN_a_stale_translation_is_invisible.md](PLAN_a_stale_translation_is_invisible.md) owns detecting staleness |
| Page tests that run the page | E3–E4 write only run-the-page tests | [PLAN_the_page_tests_run_the_page.md](PLAN_the_page_tests_run_the_page.md) owns the existing source-assertion backlog |
| The Security lane's calibration | none | [PLAN_security_lane_calibration_tail.md](PLAN_security_lane_calibration_tail.md) |

Disjoint from the rest of `todo/`. Each plan in the table gets the same row, pointing back here.

## Tails — after the redesign

| # | Tail | Why it waits |
|---|---|---|
| T1 | The old MCP server tab's sentence "reads them when your MCP client next starts it" (`panelView.ts:1864`) | The new page says the true one; T1 touches the old page only if E5 slips |
| T2 | **No security check triggers on `crypto`** — the signal is detected, no preset runs on it | A catalog change and a measurement; the routing table makes it visible first |
| T3 | Export writes the shared settings only; a side's own rows are not in the file | v2 (E1.5) exports the exporting side's rows; per-side export of the rest is its own decision |
| T4 | The mockup's own files are over 800 lines | The mockup is deleted in E5; product modules are born under the limit |
| T5 | `coai.migratedFrom` and the restore command dropped | One release after E5 |
| T6 | The extension's `vendorsFrom` turns a runtime it does not know into `codex` — an older panel reading a newer one's rows, and re-saving them so | coai-mcp refuses it by name since E2.1; the extension should keep the row and show it unrunnable, which widens the `Runtime` type |
| T7 | `COAI_BUGZ_MODEL` is written into the env block and read by nothing in coai-mcp (the collector takes `--model`) | Remove it, or give it a reader, when the Bugz picker moves to the catalog (E5.1) |

## Definition of Done

- [ ] Models is the only place a model is added, edited or removed; every feature refers to a row by id.
- [ ] A migrated install shows every reviewer, consultant, question-consultant row, chat model, Bugz model and
      security pair it had, with the same keys, prices and paths — proved by fixtures, by the real 0.43.0 binary, and
      on the operator's own install; the restore command puts the old settings back.
- [ ] coai-mcp 0.43.0 keeps working with the new extension, and the page says which controls it ignores.
- [ ] Effort reaches every runtime but Antigravity; system prompt and timeout reach every runner without ever touching
      argv or a log; the Team server carries them at contract v2 — each pinned by a test and observed once.
- [ ] A second instance of a CLI vendor probes the right program.
- [ ] Every control and state of the coverage audit (Design notes § 7) exists on the new page.
- [ ] Two columns from 1100 px, one below; side-by-side blocks one height; size and brightness on every page; "new" for
      7 days — screenshots at both widths, both themes.
- [ ] Help in five languages, `research/`, CHANGELOG and POST_DEPLOY describe the new page; `new_design/` gone.
- [ ] Every epic went through a coai plan round and code round with every finding resolved, and the session's own
      reviewers alongside; every pull-request thread resolved; the cadence consultations taken.
- [ ] Releases cut from tags after merge; post-deploy checks run against the installed builds.

## What the review changed (2026-10-04)

| Finding (who) | What changed |
|---|---|
| The old page's writes never reach the catalog; a later catalog write reverts them (gate ×2, blocking; own critic, blocking) | **The architecture.** No second store: the catalog IS `coai.vendors`, widened (D1), everything else refers by id (D2), one write road (D3). Nothing to keep in sync. |
| Downgrade/Sync edits silently overwritten (gate) | Same root, same fix; the downgrade path is tested and written down. |
| Migration ids for sources without an id; "keep every id" collides (gate; own critic) | Deterministic ids, an exact merge rule, a clash rule, fixtures (D3). |
| Which layer the derived keys go to (gate; own critic) | Migration per layer; no derived keys left to place. |
| `ModelInstance` could not round-trip today's rows: runtime, three-way `document`, chat `main`/`startingPrompt`, question rows, the shipped consultant pair (own critic) | The row is kept and widened; references keep the rest; absent = shipped stays. |
| The legacy wire cannot express the catalog (gate; own critic) | The table *What the legacy wire cannot carry*, with the note the page shows. |
| API-consultant transcripts not budgeted (gate) | A growth row with size, retirement and sweep. |
| The vault keyed by id vs a shared key (gate) | `vaultKeyName` is the key; Duplicate copies it; removal never deletes a key. |
| "Try it" and `--check-security`; a JavaScript matcher (gate; own critic) | `--check-security` in E2.4 and PROJECT.md; no JavaScript copy. |
| Regex safety unverified (gate) | NonBacktracking + timeout + caps, catastrophic-pattern RED tests. |
| System prompt on argv, in logs, to the Team server (gate; own critic) | Never argv, never logged, a cap; on the Team server behind an operator switch. |
| Effort is a free string (gate) | Levels in the shared file, validated on write, observed once per runtime. |
| ✓ Check: cost, concurrency, durable status (gate; own critic) | D10 built on `ConsultCheckState`. |
| No rollback (gate) | Backup + restore command, kept one release after the switch-over. |
| E2 too big; one coarse version gate (own critic) | Five stories, each with its own `*_SINCE`. |
| A workspace can set the model settings (own critic) | E1.1 reads the user layer and the side overlay only. |
| `COAI_MODELS` precedence and pristine output (own critic) | No new wire key at all. |
| The Team server needs a timeout too; client prompts on a shared box (own critic) | E2.5. |
| The claude-2 cause stated wrongly (fact-checker) | Rewritten: the probe runs the row id (`ReviewerRuntime.cs:226`), launches are fine. |
| Wrong line for local effort, missing `FEATURE_SINCE`, the fix site for the checkout sentence, `pageDocument`, `render-page.mjs` limits, five missing boundaries, the sidebar Bugz picker, the three replaced pages (fact-checker) | Each corrected in place. |
| The gate's commands: 4–5 epics of 3–5 stories, one gate per epic, consult on a cadence | Ten epics became five. The split was done on Opus — Fable is at its monthly spend limit (2026-10-02) — and is said so here. |

### The plan round of epic 1 (session `ac973900`, 2026-10-04, good_enough, 14 findings accepted)

| Finding | What changed |
|---|---|
| Restore cannot stick; the backup is overwritten; which keys and which layer (0, 1, 8) | Every written key backed up per layer, once; a `restored` mark stops re-migration (D3). |
| Multi-key writes are not atomic; a missing-row reference; two windows (2, 11) | A fixed write order with the marker last; rerun finds rows by id first; a missing reference is left out and logged. |
| Migrated rows reach `COAI_VENDORS` and the old page as reviewers (7) | They review nothing and are left out of the wire; the old page hides them; byte-identity measured on the overlapping fixture. |
| Prompts in an environment variable exceed its limit (4, 9) | Measured: the wire is a field whitelist, so nothing new crosses in E1; prompts never go in an env variable (D1). |
| A workspace value silently ignored; reads at some sites only (3, 10) | One reader, a scan test, a one-time notice with a copy offer. |
| Import replaces with no rollback (5) | Backup before import; the confirmation names missing vault keys. |
| The 64-row cap after migration (6) | Checked before writing; the layer is left untouched. |
| An older build strips the new fields on its first write (12) | Verified in `vendorsFrom`; what is lost and what is regained is written down; no second store. |
| `feature-availability.json` ahead of coai-mcp's own lists (13) | Frozen to today's lists in E1; the mirror tests stay. |
