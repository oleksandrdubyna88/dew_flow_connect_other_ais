# PLAN — the models are asked, rather than listed

> Status: **plan only, nothing implemented yet, 2026-09-16.** Scope: `src_vs_code/src/models.ts`,
> `panelProvider.ts`, `consultantView.ts` and a new probe module — the Claude model list, and the
> consultant's local-model dropdown.
>
> Issue: [#301](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/301).
> Related docs: [module_extension.md](../research/module_extension.md),
> [PLAN_the_consultant_has_its_own_vendors.md](../research/PLAN_the_consultant_has_its_own_vendors.md).
>
> Touches, but does not discharge, [PLAN_panel_probing_state.md](PLAN_panel_probing_state.md) — see
> *What this does not do*.

## The three asks, and what is measured about each

> «должно брать реалтайм текущие доступные модели из соотв кли (кодекс, клод, джеминай). плюс по
> умолчанию берем самую высокую версию (например фабл 5 и 5.1) если доступно. плюс для консультанта
> должно быть возможность выбрать в том числе локальную модель»
>
> *Take the currently available models in real time from the corresponding CLI (codex, claude,
> gemini). Plus, by default take the highest version — Fable 5 and 5.1, say — if it is available.
> Plus the consultant should be able to choose a local model too.*

### Fable is genuinely missing, and genuinely available

`CURATED_CLAUDE_MODELS` (`models.ts:91-95`) is `haiku · sonnet · opus`, hard-coded. Claude is the only
runtime with no discovery path at all: codex publishes `~/.codex/models_cache.json`, antigravity
answers `agy models`, a local engine answers `/api/tags`, a Team server answers a catalog. The curated
list rests on a stated theory — *"the CLI resolves an alias to the latest of that family"* — which
holds while a family only gains versions and breaks the moment a NEW family appears. Fable is a new
family.

**Measured on this machine, 2026-09-16:**

| asked for | answered by |
|---|---|
| `fable` | **`claude-fable-5-1`** |
| `sonnet` | `claude-sonnet-5` |
| `definitely-not-a-model-xyz` | `claude-opus-5[1m]` |

So Fable is available here and the picker simply does not offer it.

### The naive probe does not work, and the measurement says exactly why

The third row is the important one. **`claude --model <anything> -p "hi"` exits 0 and answers.** An
unknown model name is silently ignored and the default replies. So *"the CLI accepted it"* is not
evidence of anything, and a probe built on exit codes would report every invented name as available.

What does work: `--output-format json` returns `modelUsage` keyed by the model that **actually
answered**. The test is therefore not *did it succeed* but **did the model that answered match the one
asked for**. That also gives the concrete id for free — `fable` → `claude-fable-5-1` — so *"the
highest version, if available"* is answered by the CLI rather than assumed.

### `/v1/models` cannot be called here, and that is measured too

The operator proposed sourcing the candidate list from the API. Measured: **no Anthropic key exists in
this installation.** `ANTHROPIC_API_KEY`, `ANTHROPIC_AUTH_TOKEN` and `CLAUDE_API_KEY` are unset, and
all three configured vendors (`codex`, `gemini`/antigravity, `local`) have no key — every one runs on
its CLI's own sign-in. `ClaudeRuntime.cs:86` passes `ANTHROPIC_API_KEY` only when a vendor's key is
configured, and none is.

`/v1/models` requires `x-api-key`; the CLI's OAuth is not usable there, and digging a token out of the
OS credential store to call an API with is exactly what *"no secret ever reaches argv or a log line"*
forbids. So: **the API is the candidate SOURCE when a key is configured, and there is no key here.**
The candidates come from the curated families plus whatever the person has typed — and the probe then
turns that list of guesses into a list of facts.

### The consultant's local models

`CONSULTING_RUNTIMES` already includes `local`, and `LocalConsultant` ships — so a local model can
already BE a consultant. What is broken is the picker: `consultantView.ts:171` passes `undefined` as
`modelsFor`'s `localEngine`, so for `runtime === 'local'` the list is empty, and a model already saved
is labelled **"NOT on this engine any more"** — which is false and alarming, because nobody asked the
engine. `panelProvider` already holds `localEngines`; the consultant section needs its own, probed per
consultant ENDPOINT rather than per reviewer row, because story C5 deliberately left it holding no
reviewer rows.

## What ships

| | |
|---|---|
| `fable` in `CURATED_CLAUDE_MODELS` | the reported symptom, fixed on its own line, so it survives even if the probe never runs |
| `claudeModels.ts` (new) | the probe: ask each candidate, read `modelUsage`, keep the ones that answered as themselves, and record the concrete id |
| a week-long cache | in the data directory, so the cost is four requests a week per machine rather than four a repaint |
| the panel says it is looking | while the probe runs, because it is seconds of real requests and silence reads as an empty list |
| `consultantView.ts` | the local engine is passed through, so a local consultant has a dropdown that asked |

**The probe is bounded and its failure is not an empty list.** Each candidate is one tiny request
through `versionProbe.ts`'s `capture` — the seam that already runs `agy models` and `--version`, kills
the process TREE on timeout, and never lets a spawn error escape. If a probe fails — and it WILL, on
an account whose allowance is spent, which issue #165 has just shown is a real state here — the cache
keeps the previous answer and the curated list stands behind it. **A discovery that cannot run must
never subtract from what the person could already choose.**

## Build order

1. **RED** on the pure half, in `models.test.ts`: given a recorded probe result, `modelsFor('claude', …)`
   offers the models that answered as themselves, labels each with the concrete id the CLI resolved,
   and never offers one whose answer came back as a different model.
2. **RED** for the fallback: with no probe result at all, the curated list is offered — including
   `fable` — so the list never gets shorter than it is today.
3. The pure functions, then `fable` in the curated list.
4. **RED** on the parser: `modelUsage`'s key is read, an answer with no `modelUsage` is "unknown"
   rather than "available", and a bogus candidate whose answer names another model is refused. Fixtures
   are the real JSON shapes recorded above.
5. The probe module and its week-long cache; the panel's *looking* state; the consultant's local engine.
6. **Teeth**: invert the asked-vs-answered comparison and watch the bogus-candidate test go red; empty
   the cache-miss fallback and watch the never-shorter test go red.
7. `cd src_vs_code && rm -rf out && npm run compile && node scripts/run-tests.mjs`, then
   `node .agents/conventions/tools/plan-lifecycle.mjs` and check its exit code.

## Test plan

- No behavioural assertion over page source text (operator ruling, 2026-09-14). The pure functions are
  called; the panel's state is asserted through the page harness.
- The probe itself is NOT driven against the real CLI in the suite — it costs a billed request per
  candidate and would make the suite depend on somebody's allowance. It is tested against recorded
  answers, and that limit is written into `research/module_tests.md` rather than implied.
- Unchanged and must stay green: `models.test.ts`, `agyModels.test.ts`, `consultant.test.ts`,
  `settingsAreDeclared.test.ts`.

## What this does NOT do

- **It does not rework every probe's progress reporting.** [PLAN_panel_probing_state.md](PLAN_panel_probing_state.md)
  owns that, and it is about the local-engine probes as much as this one. What ships here is the
  saying-so for THIS probe; that plan stays open and this change makes its case stronger, since a
  model probe is seconds rather than milliseconds.
- **It does not call `/v1/models`.** Nothing here has a key to call it with, measured above. When a
  vendor key is configured the API becomes the better candidate source, and that is the open tail.
- **It does not touch codex, antigravity, local or remote discovery.** Those already ask.

## Definition of Done

- [ ] A RED test observed failing before each half, naming the real symptom.
- [ ] Inverting the asked-vs-answered comparison reddens the bogus-candidate test.
- [ ] A probe that cannot run leaves the list no shorter than the curated one — asserted, not argued.
- [ ] `fable` is offered whether or not the probe ran.
- [ ] The panel says it is looking while the probe runs.
- [ ] A local consultant's model dropdown asks the engine instead of claiming a saved model is gone.
- [ ] Whole extension suite green from a cleaned `out/`; `plan-lifecycle.mjs` clean.
- [ ] `research/module_extension.md` and `research/module_tests.md` updated, the second naming what the
      probe is NOT tested against and why.
- [ ] Promoted to `research/` with `IMPLEMENTED <date>` and its deviations; both READMEs updated.
