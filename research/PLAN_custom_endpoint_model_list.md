# PLAN — a reviewer on someone else's endpoint is offered that endpoint's models, never the Codex cache

> Status: **IMPLEMENTED, 2026-10-01.** Plan gate `proceed` (2/2), code gate `proceed` (8/8). Deviations below, in §8.
> Scope: `src_vs_code/src/models.ts`, the reviewer card in
> `panelView.ts`, one command in `panelProvider.ts`, the four other `modelsFor` callers (`chatModels.ts` ×2,
> `chatPresetsPanel.ts`, `consultantView.ts`), tests, `research/module_extension.md`.
>
> Related docs: [module_extension.md](module_extension.md), [architecture.md](architecture.md).

## 1. The symptom

Reported by the operator from a screenshot on 2026-10-01. A reviewer row named `openrouter` — runtime `codex`, base URL
`https://openrouter.ai/api/v1` — has a model dropdown reading *"the CLI's default"* and the caption
*"codex · 10 models the Codex CLI has cached for this machine."* The ten entries are OpenAI's own slugs from
`~/.codex/models_cache.json`. OpenRouter names its models `vendor/model` (`deepseek/deepseek-chat`, `openai/gpt-5`) and
accepts none of those ten, so the list offers nothing that can work and says nothing about where a working id would
come from. The person asked: *"why can't I choose a model?"*

## 2. Why — one decision, applied at one of its two sites

- `modelsFor` (`src_vs_code/src/models.ts:104`) picks a row's list by RUNTIME alone. A `codex` row gets the Codex CLI
  cache (`models.ts:177-179`) whether it talks to OpenAI or to somebody else's endpoint; the base URL is not an argument.
- The repository already decided what a hosted endpoint's list must be, for the `api` runtime (`models.ts:138-145`):
  *"neither discovered here nor curated … never the Codex cache"*, and the panel does *"not spend a paid call per
  repaint"*. The saved model is the whole dropdown, and `modelsProvenance` (`models.ts:324-328`) tells the person to run
  `coai-mcp --probe-api` in a terminal.
- A `codex` row with a base URL is the same kind of row — the Codex CLI pointed at an OpenAI-compatible endpoint through
  `CustomCodexRuntime` — and it never got that decision. Classic *a decision applied at SOME of its sites*.
- Every other `modelsFor` caller that can hold such a row inherits the defect: the chat's provider list
  (`chatModels.ts:297`) and its presets (`chatModels.ts:555`), the preset wizard's model step
  (`chatPresetsPanel.ts:396`), and the consultant picker (`consultantView.ts:243`). Only the gate-command picker
  (`panelView.ts:1534`) never sees a base URL.
- The way to ASK such an endpoint already exists and works since coai-mcp 0.40.5: `modelsForKey(executable, keyName,
  baseUrl)` (`apiModelsProbe.ts:23`) runs `coai-mcp --probe-api` — the vault read the product's own way, `GET /models`,
  ids or a reason. Today only *Add a reviewer → from the vault* calls it, once (`panelProvider.ts:3631`).

## 3. The change

1. **One road decides the list.** `modelsFor` and `modelsProvenance` take the row's endpoint as a trailing optional
   argument — `{ baseUrl, listed? }`, absent for callers that have none. A row ASKS AN ENDPOINT when its runtime is
   `api`, or when it is `codex` with a non-empty base URL (`asksAnEndpoint`, exported, the one predicate). For such a
   row the list is: the ids the endpoint listed, when it has been asked, with the saved model kept and MARKED when the
   endpoint did not list it (the local-engine three-state pattern, `models.ts:166-175`); otherwise the saved model
   alone. Never the Codex cache. The `api` arm folds into this one, so the decision exists once.
2. **The caption says where the list comes from**, in three states: not asked yet — *"the endpoint is not asked
   until you press ≡ — or choose another model… and type the id it uses"*; asked and answered — *"N models
   openrouter.ai listed for the key under 'openrouter', asked HH:MM UTC"*; asked and refused — *"openrouter.ai did not
   list its models: <reason>"*. The terminal command in today's `api` caption goes.
3. **≡ asks, on demand, never per repaint.** An endpoint card gets one button, `listEndpointModels`, beside its
   existing ones. The provider runs `modelsForKey(serverExecutable(), keyOf(vendor), vendor.baseUrl)` under a progress
   notification and keeps the answer in memory per row id; the next render passes it to the card.
   - **The key name is the row's, never its id** (plan round, gemini): `keyOf` (`apiKeyVendors.ts:105`, exported here
     rather than copied) is `vaultKeyName ?? id`, the extension's twin of the server's `KeyName`. Passing it
     explicitly matters because `--probe-api` finds its row in the settings file, which carries only ENABLED rows — a
     switched-off `openrouter-fast` filed under `openrouter` would otherwise be probed under the wrong name.
   - **An answer is bound to what it was asked with** (plan round, codex): the base URL AND the key name. A render
     whose row no longer matches both shows the not-asked state, as the local-engine probe drops a stale answer
     (`panelProvider.ts:1132-1143`). One pure function decides it, so it is tested without a host.
   - **One ask per row at a time, and the latest wins** (plan round, both): a press while that row's ask is in flight
     is ignored; each ask carries a sequence number and only the newest one is stored.
   - **Bounded, and a failure is kept and said** (plan round, codex): `modelsForKey` already caps the spawn at 45 s
     and each HTTP call at 30 s, and answers a reason rather than throwing. A refusal is stored like an answer, with
     its reason, so the caption reads "did not list its models: <reason>" rather than "not asked"; ≡ stays enabled,
     and pressing it again is the retry.
   - Nothing is persisted.
4. **Every caller passes the endpoint it holds**: the card (with the remembered answer), both chat lists, the preset
   wizard and the consultant picker (saved model only — they have no ≡). `commandModelChoices` passes nothing.

### Growth surface

One in-memory map, at most one entry per reviewer row (rows are a person-sized list), replaced on each ask and gone
on reload. No disk, no table.

## 4. Build order

1. RED: `models.test.ts` — a `codex` row with a base URL is offered no Codex-cache id; the saved model survives; a
   listed answer becomes the list; a saved model the endpoint did not list is kept and marked; the `api` row behaves
   identically (one decision); a `codex` row WITHOUT a base URL still gets the cache. Captions, three states.
2. GREEN in `models.ts`.
3. RED → GREEN per caller: the card (`panelHtml`, the existing page tests), `chatProvidersFrom` / the presets list,
   `consultantView`.
4. The ≡ button and its command; the page test RUNS the bundled page and checks the button posts
   `listEndpointModels` with the row id (repo rule: a webview is tested by running it); a host-side unit for the
   stale-answer drop through a pure helper.
5. Docs: `research/module_extension.md`; `src_vs_code/CHANGELOG.md` at release.

## 5. Test plan

The pure decisions in `models.test.ts`, each caller asserted through its own exported function, the button through
`bundledPage.test.ts`'s runner. NOT covered: the provider's handler spawning the real `--probe-api` inside an extension
host — the standing extension-host gap; the probe itself is covered by `ProbeApiModeTests` and
`apiKeyVendors.test.ts`.

## 6. Out of scope

Listing the endpoint's models in the chat and consultant pickers (they show the saved model, honestly captioned, and
"another model…" where it exists). Routing a `claude` id through a `codex` row is governed by `routableOn` and is not
changed here.

## 7. Definition of Done

- [x] `asksAnEndpoint` is the one predicate; `modelsFor`/`modelsProvenance` hold the one decision, `api` folded in.
- [x] No endpoint row anywhere is offered a Codex-cache id; a plain `codex` row still is.
- [x] ≡ lists the endpoint's models on demand; a stale answer is dropped; nothing is asked per repaint.
- [x] Every new test watched RED with the real symptom, then GREEN; teeth shown by removing the arm.
- [x] Clean `tsc`, `npm test`, the bundled-page tests green; `research/module_extension.md` updated.
- [x] Plan promoted to `research/` with its deviations when it ships.

## 8. What shipped differently (2026-10-01)

- **One ask at a time, and no sequence numbers.** §3 promised both "a press while an ask is in flight is
  ignored" and "only the newest ask is stored". The code round (codex) showed the second can never fire behind the
  first, so the sequence machinery was removed; the in-flight guard is the whole contract.
- **A probe that THROWS is kept as a refusal** (`askedOrRefused`), not only one that answers a reason — found by the
  code round (codex and gemini): an error from the spawn or the progress notification used to fly out of the command
  and leave the card on "not asked".
- **The host refuses an ask for a row that asks no endpoint** (`startEndpointAsk` checks `asksAnEndpoint`) — a
  stale or forged message for a plain CLI row no longer spawns a probe with an empty base URL (code round, gemini).
- **`keyOf` became `vaultKeyOf`, in a module of its own (`vaultKey.ts`)**, rather than being exported from
  `apiKeyVendors.ts` as §3 said (`vendors.ts`, the reviewer's suggested home, sits at the 800-line cap) — every runtime that reads a vault key asks it, and `keyOf` is a name seven other
  modules already use for unrelated things (code round, gemini).
- **`ModelChoice` moved to `modelChoice.ts`** (re-exported from `models.ts`): the new module importing it back
  from `models.ts` was a new ring that `importCycles.test.mjs` refused — a type-only import still counts.
- **Declined in the code round, with reasons on the record:** passing an endpoint to `commandModelChoices` (it is
  keyed by caller kind and holds no row); keying the answers by endpoint in a shared catalog (sharing with the chat
  and consultant pickers stays out of scope, §6); a `Set` for the in-flight rows; a fallback word for an unparseable
  time; a stricter host fallback in the caption.
- **Also done in this change:** `todo/PLAN_coai_finds_creds_where_it_is.md` and its index row now record that its
  `COAI_CREDS_KEY` defect shipped separately, in coai-mcp 0.40.5 (#627).
