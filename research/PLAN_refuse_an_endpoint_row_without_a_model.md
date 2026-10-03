# PLAN — a round refuses an endpoint row that names no model, instead of sending the Codex CLI's default

> Status: **IMPLEMENTED, 2026-10-03.** Plan gate proceed (1 of 1, gemini; 2 accepted, 3 rejected — one of them wrongly,
> see §8). Code gate proceed (4 of 4; 6 accepted, 1 partly). Deviations in §8. The server release (`mcp`, its CHANGELOG
> section on the release PR) follows the merge; no extension release is needed. Scope: `src_mcp` (the reviewer runtime that builds a
> codex row on somebody else's endpoint) and the round's refusal text. Extracted from
> [PLAN_model_search_and_busy_marks.md](PLAN_model_search_and_busy_marks.md) §4 when it was promoted.
>
> Related docs: [module_server.md](module_server.md), [module_extension.md](module_extension.md).

## 1. The symptom

A `codex` reviewer row given a base URL — OpenRouter, any OpenAI-compatible endpoint — with no model chosen runs the
Codex CLI with no `-m` (`src_mcp/runners/Reviewers/ReviewerRuntime.cs:353-354`, `ModelArgs`). The Codex CLI then sends
ITS OWN default model id to that endpoint, which does not serve it, so every review on the row fails at the endpoint
— a failure that reads as the endpoint's, not as the empty choice it is. Epic 1 of the plan this was extracted from
only stopped the panel from *labelling* that choice "the CLI's default"; nothing refuses the round.

## 2. What must be true when it is done

1. A round with an enabled codex row that has a base URL and an empty model does not launch that reviewer: it is
   reported as *cannot review* with a sentence naming the row and saying to pick a model (the panel's ≡ lists them).
2. A plain codex row (no base URL) with no model still runs on the CLI's default, as it does today.
3. `providers` says the same thing before a round is attempted, so the panel can badge the row.

## 1a. Boundary with the plan this came from

| | [PLAN_model_search_and_busy_marks.md](PLAN_model_search_and_busy_marks.md) (E1, shipped) | this plan |
|---|---|---|
| the panel's label for an endpoint row with no model | built: "no model yet — press ≡ and pick one this endpoint lists" | not touched |
| a round with such a row | not touched | refused before launch, by name |
| `providers` for such a row | not touched | reports it unavailable, with the same sentence |

## 2a. Design (2026-10-03)

**The one place that decides a row can run is `RuntimeResolution.AuthOf`** (`src_mcp/runners/Reviewers/RuntimeResolution.cs:153`).
Its own remarks say an `unavailable` answer REMOVES the vendor from the round. Both binaries reach it:
- the round and the panel, through `PanelService.AuthOf` (`PanelService.cs:309`);
- `providers` / `--providers`, through `VendorProbe` (`VendorProbe.cs:132`, `:183`).

So refusing there gives items 1 and 3 at once, and they cannot disagree.

- **`AuthOf(vendor, hasVaultKey, hasServerToken = false, model = null)`.** A codex row on an endpoint
  (`NameOf(vendor) == "codex"` and a base URL), with a key, and a known model that is empty or whitespace, answers
  `("unavailable", "<id> points the Codex CLI at <base URL> with no model — pick one that endpoint lists (≡ on its
  card); with none, the Codex CLI sends its own default model id, which that endpoint does not serve")`.
- **`null` means the caller does not know the model.** The check is then skipped, which keeps every existing caller
  and test exactly as it is.
- **The model stays out of `VendorIdentity`.** Its remarks rule that out: a model belongs to a launch, and widening
  an identity is how a type starts meaning two things. It travels as a parameter, the way `hasServerToken` does.
- **The key check stays first.** A row with neither a key nor a model is told about the key, as today; once the key
  is in, the model sentence follows.
- **`ExclusionReason`** (the sentence a MODEL reads in round status) gains the same parameter and says "no model is
  chosen for its endpoint". It stays written by this side, never the row's own text.
- **Callers pass the model:**
  - `PanelService.AuthFor` and `ReasonFor`, from `ProviderSettings.Model`;
  - `VendorProbe`'s CLI path, which gains the model argument its API path already has.
- **A plain codex row (no base URL) never sees the check** (item 2). Neither does `api`, which has its own model check
  in `ApiHealth`, nor `local` or `remote`.

**Found while building: no extension change.** The reviewer card already draws "<id> cannot review: <note>" for any row
`providers` answers `unavailable` (`panelView.ts` `cannotRun`, `providers.ts` `availabilityOf`), and the note names the row,
the endpoint and the cure. Item 3 needs nothing on the extension side, so there is no extension release.

## 3. Build order

1. RED: a runtime test that a codex row with a base URL and no model is refused before launch, naming the row.
2. The refusal in the one place that decides a row can run: `RuntimeResolution.AuthOf` (§2a). Not where `CustomCodexRuntime`
   is built, because `providers` never builds one; and no second check there (plan round, gemini).
3. `providers` reports it; the panel's existing "cannot review" badge shows it.
4. A server release (`mcp-v*`), then the extension's help text if it changes.

## 4. Test plan

The runtime test above, its teeth checked by deleting the refusal; a `providers` contract test; the extension's card
test that the badge appears for such a row.

## 5. Growth surfaces

None.

## 6. Definition of Done

- [x] RED first, then GREEN, teeth checked; the whole MTP suite green.
- [x] `providers` and the round agree.
- [x] Docs updated; this plan promoted.
- [ ] A server release cut and verified — after the merge, on the release-please PR.
- [x] The reciprocal boundary table is in `research/PLAN_model_search_and_busy_marks.md`.

## 8. Deviations — what shipped differently

- **`ReadinessOf`, not a parameter on `AuthOf`.** §2a put a nullable `model` on `AuthOf`. The code round rejected it:
  doctrine §4 allows no such nullable, and `ExclusionReason` had to reach around `AuthOf` to tell the two reasons
  apart. `AuthOf` is unchanged (credentials only). `ReadinessOf(vendor, hasVaultKey, hasServerToken, string model)`
  answers for a configured row, and an `ExclusionReason` overload takes the model.
- **Null is no model.** The plan round's finding that an omitted field arrives null was REJECTED with "Model is never
  null", and that rejection was wrong: doctrine §4a measured exactly this. The boundary (`ParseVendors`,
  `PanelSettings.cs:1182-1183`) does coalesce to empty, and the rule now treats null, empty and whitespace alike. A
  test pins it.
- **No extension change.** The card already badges any `unavailable` row with its note (§2a).
