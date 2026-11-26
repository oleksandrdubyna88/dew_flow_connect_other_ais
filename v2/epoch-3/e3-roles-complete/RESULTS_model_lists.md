# E3 s4 t1 - the model names of the installed codex and agy, as the CLIs print them

Measured 2026-11-25 (epoch 3 E3 s4 t1). Facts only, no product code. **No model was called** to make this file: both lists are read from a
command that answers from a catalog (`codex debug models`, `agy models`). A name that only a web page shows is not here (d2).

Installed releases: `codex-cli 0.171.0` (`~/.codex/packages/standalone/releases/0.171.0-x86_64-unknown-linux-musl/bin/codex`),
`agy 1.5.17` (`~/.local/bin/agy`, 278 438 600 bytes; SPEC section C3 read `1.5.16` earlier the same day: **the installed release moved to
1.5.17 between that read and this measure**, and the CLI updates itself, so the release in every row of the s4 results is the one named in
its heading). WSL2, node 26.12.1.

Credential used: the owner's SUBSCRIPTION sign-in, as a COPY in a scratch home (owner decision 2026-11-25: no API key exists for either
vendor). How that was shown safe is in `v2/spikes/RESULTS_codex_coder.md` section 0 and `v2/spikes/RESULTS_agy_coder.md` section 0.

## codex 0.171.0

Two places, read with a scratch `CODEX_HOME` (never the real one):

| Place | Command | What it is |
|---|---|---|
| bundled catalog | `codex debug models --bundled` | the catalog compiled into the binary: no network, no sign-in needed (rc 0, 920 320 bytes) |
| live catalog | `codex debug models` (scratch home holding a copy of the sign-in) | what the subscription is offered today (rc 0) |

Names as printed (field `slug`; `visibility` as printed):

| slug | bundled | live (this account) | visibility | description (first words) |
|---|---|---|---|---|
| `gpt-6.2-sol` | yes | yes | list | Fast, go-to model for coding and everyday work |
| `gpt-6.2-astra` | yes | yes | list | Our most capable model for complex coding |
| `gpt-6.1-terra` | yes | yes | list | Efficient model that excels at visual understanding and computer use |
| `gpt-6.1-sol` | yes | yes | list | Previous generation model for coding and everyday work |
| `gpt-6-astra` | yes | yes | list | Previous generation model for complex coding |
| `gpt-6-sol` | yes | yes | list | Legacy model for coding and everyday work |
| **`gpt-6-luna`** | yes | yes | list | **Fast and affordable model for easier tasks** (efforts low..max; no `ultra`) |
| `gpt-5.6-sol` | yes | yes | list | Legacy model for coding and everyday work |
| `gpt-5.6-terra` | yes | yes | list | Legacy balanced model for agentic coding |
| `gpt-5.6-luna` | yes | yes | list | Legacy fast model for simpler tasks |
| `gpt-5.5` | yes | no | list | Legacy coding model |
| `gpt-daybreak-blue-latest`, `gpt-daybreak-red-latest` | yes | no | hide | cyber-security variants |
| `codex-auto-review` | yes | yes | hide | Automatic approval review model |
| `gpt-reserve` | no | yes | hide | (live only; no description read) |

The owner's real `~/.codex/models_cache.json` (read only, `fetched_at 2026-11-25T14:08Z`, `client_version 0.171.0`) holds the same live list.
The model the owner's `config.toml` names is `gpt-6-astra`.

**Smallest model of codex: `gpt-6-luna`** (the vendor's own words "fast and affordable ... easier tasks"; the "luna" tier is the lowest of the
sol / terra / luna / astra family in every generation). Used with `-c model_reasoning_effort=low`. Every paid call of the codex measure uses it.
SPEC section D proposes `terra` and `astra` for the scout and important lists: both names exist as `gpt-6.1-terra` / `gpt-6.2-astra` /
`gpt-5.6-terra` / `gpt-6-astra` (no bare `terra` or `astra`; the repository's `codex-astra` was a pinned name, `shared/runtime-capabilities.json`).

## agy 1.5.17

Command: `agy models` with a scratch HOME holding a copy of the sign-in. rc 0, 27 lines, `<id><TAB><display name>`. With a HOME that holds **no**
sign-in it answers `Error: Please sign in to view available models. Launch the CLI without arguments to sign in.` (rc 1): the list needs the sign-in.

```
gemini-4-neon-high   gemini-4-neon-xhigh   gemini-4-neon-medium   gemini-4-neon-low
gemini-4-carbon-medium   gemini-4-carbon-high   gemini-4-carbon-low
gemini-4-argon-medium    gemini-4-argon-high    gemini-4-argon-low
gemini-4-hydrogen-high   gemini-4-hydrogen-xhigh   gemini-4-hydrogen-medium   gemini-4-hydrogen-low
gemini-pro-orchestrator-neon   gemini-pro-orchestrator   gemini-pro-orchestrator-argon
gemini-3.8-flash-high   gemini-3.8-flash-medium   gemini-3.8-flash-low
claude-opus-5-5-low|medium|high   claude-sonnet-5-5-low|medium|high      (Claude models: the vendor-routing rule never runs them on agy)
gpt-oss-120b-medium
```

Display names are `Gemini 4 Neon (High)` ... `Gemini 3.8 Flash (Low)`, `Orchestrator (Neon|Carbon|Argon)`. agy prints no price or size.

**Smallest model of agy: `gemini-3.8-flash-low`.** The "Flash" tier is the cheap tier of the family, `low` is its lowest effort, and the
repository already makes it agy's default model (`AntigravityRuntime.DefaultModel`, `AntigravityDefaultModelTests`). "Smallest" is a
judgement from the names (the CLI states no size); every paid call of the agy measure uses it. SPEC section D's `gemini-4-argon-medium` for the
coder failover is in the list.

## Procedure (repeatable, C2)

1. `HOME=<scratch> CODEX_HOME=<scratch>/codex codex debug models --bundled` and again without `--bundled` with a copy of the sign-in.
2. `HOME=<scratch with a copy of the sign-in> agy models`.
3. Compare with the previous release's list; a name that left the list is a row that must be re-measured.

Spend of this task: 0 model calls. 3 `agy models` and 2 `codex debug models` (network, no model).
