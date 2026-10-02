# PLAN — a round refuses an endpoint row that names no model, instead of sending the Codex CLI's default

> Status: **plan only, nothing implemented yet, 2026-10-02.** Scope: `src_mcp` (the reviewer runtime that builds a
> codex row on somebody else's endpoint) and the round's refusal text. Extracted from
> [PLAN_model_search_and_busy_marks.md](../research/PLAN_model_search_and_busy_marks.md) §4 when it was promoted.
>
> Related docs: [module_server.md](../research/module_server.md), [module_extension.md](../research/module_extension.md).

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

## 3. Build order

1. RED: a runtime test that a codex row with a base URL and no model is refused before launch, naming the row.
2. The refusal in the one place that decides a row can run (sweep by shape: wherever `CustomCodexRuntime` is built).
3. `providers` reports it; the panel's existing "cannot review" badge shows it.
4. A server release (`mcp-v*`), then the extension's help text if it changes.

## 4. Test plan

The runtime test above, its teeth checked by deleting the refusal; a `providers` contract test; the extension's card
test that the badge appears for such a row.

## 5. Growth surfaces

None.

## 6. Definition of Done

- [ ] RED first, then GREEN, teeth checked; the whole MTP suite green.
- [ ] `providers` and the round agree.
- [ ] Docs updated; a server release cut; this plan promoted.
