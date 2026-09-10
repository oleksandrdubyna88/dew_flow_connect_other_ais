# PLAN — pick the provider, then the model

> Status: **IMPLEMENTED, 2026-09-10.** The pure half shipped 2026-09-09 (another lane, PR #162); the
> two selects, the wiring and the host resolution shipped in PR #175. Kind: **feature**.
> Origin: [../todo/BUGS_2026-09-09.md](../todo/BUGS_2026-09-09.md), entry 21.
>
> ### What shipped differently
>
> **The PANEL half did not ship with the rest, and nothing here said so — corrected 2026-09-10.**
> The Definition of Done below reads "in the tab and in the panel"; the shape section says "the
> panel's *Which model answers* becomes the same pair". PR #175 shipped `chatPickerHtml` and left
> `panelView.ts` alone — the pure half's own commit had said the two selects belonged to other lanes,
> and only one of those lanes ran. This record was written as though the plan were complete, which is
> the worse half of the omission: a missing feature is found by using the product, a missing deviation
> is found by nobody. The panel asks provider-then-model now, with `coai.chatModelName` for the second
> half, and `openingModel` deciding what a new tab opens with.
>
> **`coai.chatModelName`, not the `coai.chatProvider` this plan named.** The reason is below: the
> provider was already saved, so the half that had no home was the MODEL. `coai.chatModel` keeps
> holding a row id — every installation's value keeps meaning what it was written to mean — and the
> new setting names one of that row's models, with the row's own model as the fallback.
>
> **The panel builds its own catalog, which closes the open tail below for this surface.** The
> discovered codex and agy lists and a Team server's allowlist are already in the panel; the chat
> command's `chatCatalogFrom` passes them empty. The section therefore offers a discovered row all of
> its models where the command still offers one. The tail stands for the command.
>
> **There is no `coai.chatProvider` setting.** The plan named one; what shipped keeps the pair on the
> conversation and migrates the existing `coai.chatModel` — which has always held a ROW id — through
> `legacyPick`. A second setting would have been a second thing to keep in step with the first, for a
> value that is already saved.
>
> **A provider is a vendor ROW, not a runtime**, and that was the pure half's round rather than this
> plan's: three vendors' reviewers independently overturned the recommendation written here.
>
> **A lone provider is still shown.** The flat list hid itself at one entry, and one old test changed
> its GUARANTEE rather than its shape — with two steps there is always something to choose.
>
> ### The open tail
>
> **Discovery.** Three of the four model sources are FETCHED rather than read — a local engine's list,
> the codex and agy CLIs' own lists, and a Team server's allowlist — and all three live in the panel,
> which has already done that work. The chat command has none of it, so a row whose list must be
> fetched offers the model it is configured to and nothing else, which is exactly what the flat list
> offered. Handing the panel's discovered lists across is the next step, and it is the only thing
> between this and a real choice for every row.

## The goal

Two steps instead of one: choose the PROVIDER (codex, claude, google — and any Team server), then
the MODEL. On a Team server only what that server allows is offered.

## Why the picker reads as an arbitrary handful — the root

It is not a list of models. `chatModelsFrom` (`chatModels.ts:111-129`) maps each **configured
vendor row** to one entry labelled `${vendor.id} · ${vendor.model}`, so `gemini ·
gemini-3.8-flash-low` is one reviewer row with whatever model that row is set to. A different
model means going to the panel and re-configuring a reviewer.

## What is already there — this is assembly

- `modelsFor(runtime, discoveredCodex, current, localEngine, discoveredAgy, allowedRemote)`
  (`models.ts:94`) is exactly "the models of one provider": discovered for codex, agy and a local
  engine; curated for Claude (`CURATED_CLAUDE_MODELS`, `models.ts:88`); and for a Team server the
  server's own allowlist, with a saved model the server withdrew KEPT and MARKED rather than
  dropped (`models.ts:105-112`) — the "only what is allowed" rule, already built.
- `allowedModelsFor(vendor, teamServers)` (used at `panelView.ts:407`) supplies that allowlist.
- The reviewer card is already a provider → model pair of controls (`vendorCard`,
  `panelView.ts:598-635`). The chat picker should look like the thing that already works.

## The decision — a row, or a pair of its own

Today the chat picks a configured ROW, and everything downstream assumes it: the row carries the
runtime, the executable, the base URL, the price, and for a Team server the server AND
`remoteVendor`; the boundary check "refuses a pick naming a model the conversation was not
offered" (`chatModels.ts:8-11`) exists on that basis.

**Recommendation, to be confirmed by the gate and the operator:** the chat gets a **pair of its
own** — `coai.chatProvider` (a runtime id, or a Team-server id) and the existing `coai.chatModel`
— resolved to a vendor row for HOW to run it (first enabled row of that runtime; for a server, the
row for that server), with the picker supplying WHICH model. A model no reviewer row uses becomes
pickable, which is the point.

## Two traps

1. **`remoteVendor` is the field this family has already lost once** — dropped silently by every
   component written before it existed, for three releases
   (`research/PLAN_team_server_reviewer_never_called.md`). A picker that selects a Team server must
   carry the server AND the vendor name on that server, or it reproduces that failure: a 400
   `'<server>-<vendor>' is not a vendor here`, in seconds, for no tokens. A contract-suite case
   (`npm run test:contract`) for a chat sent with a chosen remote provider is part of this plan.
2. **A model must not reach the wrong adapter.** `vendor-routing.md` is explicit that a Claude
   model never goes through `agy`; the adapter map chooses adapter and executable together
   (`chatModels.ts:15-21`). A free provider/model choice makes a combination with no adapter newly
   possible — refuse it BY NAME, the way `refused` rows already are.

## The shape

- `chatModelsFrom` becomes `chatProvidersFrom(vendors, teamServers)` → providers (each with its
  models via `modelsFor`, and a `refused` reason where it cannot chat) — pure, tested.
- `chatPickerHtml` renders two selects; changing the provider re-fills the model select from the
  pushed catalog (the `live` message pattern `chatPanel.ts:85-92` already pushes model lists).
- The panel's *Which model answers* becomes the same pair; `strandedOption`
  (`panelView.ts:274`) keeps naming a saved choice that can no longer answer.
- `chosenModel` (`chatModels.ts:138`) and the panel-boundary check work on the pair.
- The ledger records the resolved row AND the chosen model, so
  [PLAN_who_said_it_and_what_it_cost.md](PLAN_who_said_it_and_what_it_cost.md) has the truth.

## Build order

1. RED tests (below).
2. `chatProvidersFrom` + the resolution function (pair → row) — pure, in `chatModels.ts`.
3. The two settings; `chatSettingsFrom` reads them with fallbacks stated (`chatSettings.ts:60`).
4. The page picker and the panel control.
5. The remote case with `remoteVendor`, and the contract-suite case.
6. GREEN; whole suite; contract suite; a manual pick of each provider, recorded.

## Test plan

- `chatModels.test.ts`: providers from a vendor list; a Team server's models are its allowlist;
  a withdrawn saved model is kept and marked; a pair with no adapter is refused by name; the
  resolution picks the right row and carries `remoteVendor`.
- `chatSettings.test.ts`: fallbacks for a missing/garbage `chatProvider`.
- `chatPage.test.ts`: two selects; the model select matches the chosen provider.
- Contract: a remote chat request carries `remoteVendor`.

## Acceptance — one PR per plan, and the gate on both sides of it

This plan ships as **its own branch (`feat/provider-then-model`) and its own pull request**, and the PR is accepted
only when the whole ritual has run — not when the code works.

1. **Gate, before the first line of code.** `open` a coai session for this repository and the
   branch; `review_plan` with THIS file as the plan; `resolve` every finding (a rejection carries a
   reason); repeat until the verdict is `proceed`. `review_code` refuses without it.
2. **Tests first.** A RED test per defect, watched failing with the real symptom, then GREEN; every
   new behaviour with its happy path and the failure paths it introduces — `npm test` in
   `src_vs_code`, the whole suite, not the one file.
3. **Gate, after the code.** `review_code` with the scope = the *Definition of Done* below plus the
   goal, and the diff `main...feat/provider-then-model` — three dots; the two-dot moving-base trap is recorded in
   `research/PLAN_the_gate_diffs_from_a_moving_base.md`. `resolve`; repeat until `proceed`.
4. **Documentation.** `research/module_extension.md` says what the code now does;
   `research/architecture.md` if a cross-module seam moved.
5. **Help.** Every new command and setting has an article in `helpContent.ts` —
   `helpCoverage.test.ts` fails the build otherwise; a lagging translation is marked as such.
6. **README and CHANGELOG.** `src_vs_code/README.md` if what a person sees changed;
   `src_vs_code/CHANGELOG.md` in the prose the file already uses — the sentence a person reads,
   not the commit subject.
7. **Manifest.** `package.json` contributions (settings, commands, keybindings, menus), and the
   version the release line expects (see the `chore(release)` history).
8. **Family checks.** `node .claude/rules/shared/tools/plan-lifecycle.mjs` and `pin-check.mjs`
   clean.
9. **Promote.** On merge, `/promote-plan` this file to `research/` with `IMPLEMENTED <date>` and
   every deviation recorded — what shipped differently is the most valuable line of the record.

**Specific to this plan:** a NEW SETTING `coai.chatProvider` → manifest entry + help article (helpCoverage enforces it); the chat help article's picker paragraph rewritten; `module_extension.md`'s chat section gains the pair and the resolution; CHANGELOG in the person's words; migration: an existing `coai.chatModel` that names a vendor ROW id keeps working (resolved to that row) until re-picked.

## Definition of Done

- [ ] Provider first, then model, in the tab and in the panel; a Team server is a provider whose models are its allowlist.
- [ ] A saved choice the server withdrew is shown and marked, never silently swapped.
- [ ] A pair with no adapter is refused by name; a Claude model never reaches `agy`.
- [ ] A remote choice carries `remoteVendor` — proven by the contract suite.
- [ ] The old single `chatModel` value still resolves.
- [ ] `npm test` and `test:contract` green; the acceptance ritual complete; promoted on merge.

## Parallelism

Owns `chatModels.ts`, the two settings, and `chatPickerHtml` in `chatPage.ts`. Queues behind
[PLAN_the_composer_stays_put.md](PLAN_the_composer_stays_put.md) in the chat-page lane;
**[PLAN_presets_above_the_composer.md](PLAN_presets_above_the_composer.md) depends on this one** —
its model presets are pairs. The pure half (`chatProvidersFrom`, resolution, tests) has no
conflicts and can be written ahead of the lane.
