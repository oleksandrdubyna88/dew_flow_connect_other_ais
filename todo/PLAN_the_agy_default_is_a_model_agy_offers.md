# PLAN — the Antigravity default is a model agy offers

> Status: **plan only, nothing implemented yet (2026-10-21).** Found by the E1 measurement of
> `PLAN_the_round_counter_tail.md` (in #904); the owner chose the new default on 2026-10-21.

## The symptom

A reviewer row with `runtime: antigravity` and no model of its own launches `agy --model gemini-3.7-flash-high`
(`AntigravityRuntime.cs:37` `DefaultModel`, used at `:131`). agy 1.4.4 refuses it before any model call:
`invalid model selection ... not recognized` (seven launches, 0 tokens, 2026-10-21). `agy models` on 1.4.4 lists
`gemini-4-argon-{low,medium,high}`, `gemini-3.8-flash-{low,medium,high}`, `claude-opus-5-5-*`, `claude-sonnet-5-5-*` and
`gpt-oss-120b-medium` — no 3.7. The extension's curated fallback list (`src_vs_code/src/models.ts:262-264`) still offers
only the three 3.7 ids when `agy models` cannot be asked.

## What must be true

1. `AntigravityRuntime.DefaultModel` is `gemini-3.8-flash-low` — the owner's choice (2026-10-21), the model the live agy
   reviewer row already runs.
2. The curated fallback in `models.ts` offers the 3.8 Flash ids (low, medium, high) instead of the 3.7 ones; `agy models`,
   when it answers, still wins (unchanged).
3. Comments that name `gemini-3.7-flash-high` as THE example of an effort-in-the-id model (`AntigravityRuntime.cs:23`,
   `SessionStore.cs:37-38`, `rounds.ts:40`, `modelPrices.ts:88`, `shared/feature-availability.json:55,112` and its generated
   copy) name a model agy offers. Test fixtures that use 3.7 as an arbitrary id stay as they are — they test parsing, not
   the catalogue.
4. A saved row whose model is a 3.7 id is NOT rewritten silently; agy refuses it with its own sentence, as today. (A
   migration is a separate decision, not asked for.)

## Stories (one PR, coai-mcp + extension)

- **S1 (C#)** — RED: a test that `new AntigravityRuntime().Build(...)` for a row with no model sends `--model
  gemini-3.8-flash-low` (today `gemini-3.7-flash-high`). Then change the constant.
- **S2 (extension)** — RED: `agyModels.test.ts` / the curated-list test expects the 3.8 Flash ids in the fallback (today
  3.7). Then change `models.ts:262-264`.
- **S3 (docs/comments)** — the comments and the feature-availability notes; regenerate `featureAvailability.generated.ts`
  by the repo's own generator (never by hand).

## Test plan and DoD

- [ ] S1 and S2 RED for the real reason, GREEN, RED again with the change reverted.
- [ ] Whole src_mcp suite (the MTP exe) and extension `npm test` green; the generated file matches its generator.
- [ ] coai plan and code rounds `proceed`, findings resolved; `research/module_runners.md` names the default.
- [ ] Ships in the next coai-mcp and extension releases; the changelog names the old and the new default.
