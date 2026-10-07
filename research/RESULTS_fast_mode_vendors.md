# RESULTS — "fast mode" across the vendors coai runs (2026-10-06)

> Status: **record, 2026-10-06, before any code.** The research behind the owner's request of 2026-10-06: a per-model
> "fast mode" switch on the NEW Settings page's model card, for codex, claude and any other vendor that has one, off
> by default. Read from each vendor's documentation and CLI help on 2026-10-06; the links were not kept in that pass,
> so each fact is re-checked against its source when the plan is written. **Nothing here is measured yet** — no call was
> made in fast mode, and no speed or price was observed. See *Not measured*.

## What each vendor offers

| Vendor / runtime | Fast mode? | How it is asked for | Cost and limits (as documented) |
|---|---|---|---|
| Claude Code CLI (`claude` runtime) | **yes**, from CLI 2.1.205 | `claude -p --settings '{"fastMode":true}'`; the result events carry `fast_mode_state` | Opus 5.5, Opus 5 and Opus 4.8 only; about **2×** the price, paid from usage credits |
| Claude API (Messages) | **yes** | header `anthropic-beta: fast-mode-2026-02-01` and `"speed": "fast"` in the body | same models and price as above |
| Codex CLI (`codex` runtime) | **yes** | `-c service_tier="fast"` (the same tier as `priority`); `-c service_tier="default"` forces standard; `flex` is cheaper and slower | about **2–2.5×** usage |
| OpenAI API (`api` rows on OpenAI) | **yes** | `service_tier`: `fast` / `priority` | priority pricing |
| xAI API (`api` rows on xAI) | **yes** | `service_tier`: `fast` / `priority` | priority pricing |
| Antigravity (`agy`) | no | — | — |
| DeepSeek API | no | — | — |
| GLM (Zhipu / z.ai) API | no | — | — |
| Qwen (Alibaba Model Studio / Token Plan) API | no | — | — |

## Found on this machine

`~/.codex/config.toml` has `service_tier = "priority"`. The Codex CLI reads that file on every launch. So **coai's
codex reviewers and the codex consultant very likely run in fast mode today**, at about 2–2.5× usage, without anyone
choosing it for coai. This was read from the config file; the extra spend has not been measured.

## The owner's decision (2026-10-06)

Asked whether coai's "off" should override the CLI's own setting, the owner answered: a setting made for ALL uses of
the CLI must be overridden, but switching fast on in the CLI should switch it on for coai too. These two are the same
file for codex (`/fast` writes `service_tier` to `config.toml`) and for Claude Code (`/fast` writes `fastMode` to its
user settings), so coai cannot tell them apart. The owner accepted three states:

| State on the model card | What coai sends |
|---|---|
| **Off** (the default) | forces standard: codex `-c service_tier="default"`, claude `--settings '{"fastMode":false}'`, api rows no fast tier |
| **On** | forces fast: codex `-c service_tier="fast"`, claude `--settings '{"fastMode":true}'`, OpenAI / xAI `service_tier` |
| **As the CLI is set** | nothing — the CLI's own configuration applies |

The control is drawn only for a runtime and model that has a fast tier.

## Not measured

- The speed-up and the real price of fast mode on any vendor — no call was made.
- How much of coai's past codex spend was the `priority` tier.
- Whether `--settings '{"fastMode":false}'` overrides a user's `fastMode: true` in Claude Code's settings (expected from
  the documented precedence of `--settings`; to be confirmed on the installed CLI).
- Whether `-c service_tier="default"` is accepted by every installed codex version coai supports.
