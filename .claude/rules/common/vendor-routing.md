# Vendor routing — a model runs on its OWN vendor's CLI (MANDATORY)

> Every model this product can review with is reachable through more than one CLI, and the choice is
> not free. This rule fixes it, because getting it wrong is invisible in the output and expensive in
> the bill.

## The rule

**A Claude model runs through the `claude` CLI. Never through `agy`, never through `codex`.**

Antigravity's subscription bundles Gemini, Claude and GPT-OSS behind one CLI, so
`claude-sonnet-4-6` and `claude-opus-4-6-thinking` are selectable there. They must not be selected
there. The same models are on this operator's own Claude subscription, which is unlimited, while
every antigravity call is drawn against a quota that is neither unlimited nor cheap.

The same reasoning in general form: **route a vendor's model through that vendor's own CLI** unless
there is a stated reason not to. A reviewer row exists per vendor precisely so this is a
configuration and not an accident.

| model family | the CLI it runs on | never |
|---|---|---|
| Claude (haiku, sonnet, opus) | `claude` | `agy`, `codex` |
| GPT-5.x (Terra, Sol, Luna, 5.5, 5.4) | `codex` | `agy` |
| Gemini 3.x, GPT-OSS | `agy` | — |

## What it cost to learn

The model-comparison campaign of 2026-09-01 ran Claude Sonnet 4.6 and Claude Opus 4.6 through
`agy`. Fifty runs in an hour exhausted the antigravity account's quota — `Individual quota reached`
— and three cells of the code half were lost with it, including both Claude models, whose native
runtime was sitting idle on an unlimited plan the whole time.

Two things worth keeping from that. **The waste was invisible in the results**: the reviews came
back normally and looked like every other row, so nothing in the output said the wrong CLI had been
used. And **the failure was mine, not the models'** — the doc first recorded three missing cells as
a fact about rate limits before the log was read properly.

## Never

- Do **not** pick a Claude model from the antigravity model list because it is in the list.
- Do **not** spend a metered quota on a model that has an unmetered route.
- Do **not** report a vendor's quota failure as a property of a model.

## Definition of Done

- [ ] Every reviewer row runs its model on that model's own CLI.
- [ ] A measurement that spans vendors says which CLI each model ran on.
- [ ] A quota or rate-limit failure is reported as what it is, after reading the log.
