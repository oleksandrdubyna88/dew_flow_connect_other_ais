# PLAN — the Antigravity default is a model agy offers

> Status: **IMPLEMENTED, 2026-10-21.** Plan and code rounds proceed. Deviations: the extension's own copy of the default (`ANTIGRAVITY_DEFAULT_MODEL`, the `coai.vendors` manifest default) was found while coding and is changed with it, tied to the C# constant by a test; two existing tests whose fixtures assumed the old catalogue were adjusted. Open tail: the other stale curated ids (Claude 4.6, 3.1 Pro) and saved 3.7 rows are left as they are. Found by the E1 measurement of
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
   reviewer row already runs. **The default assumes agy 1.4.4 or later** (where `agy models` lists the 3.8 Flash ids); on an
   older agy a row must name its model.
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
- **S2 (extension)** — the extension has its own copy of the decision: `ANTIGRAVITY_DEFAULT_MODEL` (`vendors.ts`, used by
  `DEFAULT_VENDORS`, the Antigravity preset and the retired-Gemini migration) and the `coai.vendors` default in
  `package.json`; both become `gemini-3.8-flash-low`, and `antigravityDefaultModel.test.ts` reads the C# constant so the
  halves cannot drift. RED: `agyModels.test.ts` / the curated-list test expects the 3.8 Flash ids in the fallback (today
  3.7). Then change `models.ts:262-264`.
- **S3 (docs/comments)** — the comments and the feature-availability notes; regenerate `featureAvailability.generated.ts`
  by the repo's own generator (never by hand). Enumerated with `git grep -n "gemini-3.7" -- src_mcp src_server src_vs_code/src shared research README* ':!todo'`
  — 81 lines before, 66 after: the 15 changed are comments, the feature-availability notes (+ the regenerated copy) and
  the current-default rows of the research docs; what remains is test fixtures using 3.7 as an arbitrary id and
  historical records (measurement results, the log plan, an incident narrative).
- **Pricing** — the repo carries no typed price for any Gemini model: prices are looked up in OpenRouter / LiteLLM
  (`modelPrices.ts`), and the only `gemini-3.7-flash` prices are test fixtures. So no 3.8 entry is added (no public list
  price for 3.8 is used anywhere in the repo to cite). The new default prices as "no list price" — a floor, like any
  unknown model — until a list carries `gemini-3.8-flash`; `modelPrices.test.ts` pins that, and that every effort of the
  model is priced by the one entry the day a list has it.
- **Live check (2026-10-21, agy 1.4.4, one launch from an empty folder)** — `agy --print="Reply with exactly: OK" --model
  gemini-3.8-flash-low --output-format json`: accepted, exit 0, answer `OK`, 1 output token.

## Test plan and DoD

- [ ] S1 and S2 RED for the real reason, GREEN, RED again with the change reverted.
- [ ] Whole src_mcp suite (the MTP exe) and extension `npm test` green; the generated file matches its generator.
- [ ] coai plan and code rounds `proceed`, findings resolved; `research/module_runners.md` names the default.
- [ ] Ships in the next coai-mcp and extension releases; the changelog names the old and the new default.
