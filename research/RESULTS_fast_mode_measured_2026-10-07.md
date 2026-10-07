# RESULTS — fast mode, measured on this machine before any code (2026-10-07)

> Status: **record, 2026-10-07, Story 0 of [PLAN_fast_mode.md](../todo/PLAN_fast_mode.md) — in progress.** The plan
> gate asked for the spellings to be measured BEFORE the server, the wire and the card are built on them. Each call
> below sent only the prompt "Reply with the single word OK." — no repository content — through the person's own
> CLI and its own sign-in. Raw outputs were kept in the session's scratch folder, not in git (they hold account ids).

## The machine

| | |
|---|---|
| codex | `codex-cli 0.160.0`, model `gpt-6.1-sol` (its default) |
| claude | Claude Code `2.1.289`, Windows |
| `~/.codex/config.toml` | `service_tier = "default"` — **not** `"priority"` as on 2026-10-06 ([RESULTS_fast_mode_vendors.md](RESULTS_fast_mode_vendors.md)); the line was changed in between |

## codex: `-c service_tier=<value>`

| Run | Exit | What the event stream said |
|---|---|---|
| `codex exec --json -c service_tier=fast "…"` | 0 | answered; `turn.completed` with usage (15 293 in, 12 544 cached, 5 out) — no tier field |
| `… -c service_tier=default` | 0 | answered; usage (14 483 in, 12 544 cached, 5 out) — no tier field |
| `… -c service_tier=bogus` | 0 | answered, after an `error` ITEM: *"Configured service tier `bogus` is not advertised as supported for model `gpt-6.1-sol` and will be omitted from requests."* |

**What this settles:**
- codex READS `service_tier` from `-c`, and checks the value against what the model advertises.
- `fast` and `default` are both supported for `gpt-6.1-sol`: neither drew the warning.
- An unsupported value is **dropped with a warning and the run goes on**. It is not a refusal, so an older CLI or
  another model will not fail a review over this flag.
- The unquoted spelling works on Windows through the npm shim.

**What it does not settle:** neither the `--json` stream nor the session file (`~/.codex/sessions/…/rollout-*.jsonl`,
searched for `service_tier`) records which tier a run used. So Off vs On cannot be told apart from the output. The
proof available is the absence of the warning (the tier was sent), and speed or usage over many runs.

## claude: `--settings` with `fastMode`

| Run (all `claude -p --model opus --output-format json`) | Exit | `fast_mode_state` | Notes |
|---|---|---|---|
| `--settings <file>` holding `{"fastMode":false}` | 0 | `off` | answered `OK`, 2 236 ms |
| `--settings <file>` holding `{"fastMode":true}` | 0 | **`off`** | answered `OK`, 1 630 ms |
| `--settings '{"fastMode":true}'` (inline) | 0 | **`off`** | answered `OK` |

`claude --help` (2.1.289) does not mention fast mode at all.

All three results carry `fast_mode_disabled_reason: "preference"`.

**The documentation** (read 2026-10-07: code.claude.com/docs/en/fast-mode.md, settings-reference.md, cli-reference.md,
agent-sdk/typescript.md) says the spelling IS right: *"in non-interactive mode with the `-p` flag, `/fast` works only in a
session launched with fast mode in its `--settings` value, for example `claude -p --settings '{"fastMode": true}'`"*
(v2.1.205+); `fast_mode_state` is `off` | `cooldown` | `on` (v2.1.219+), with `fast_mode_disabled_reason` when off.
Fast mode needs an Opus 5.5 / 5 / 4.8 model, usage credits on a subscription plan, and — on Team/Enterprise — an
Owner's enablement; a `fastModePerSessionOptIn` setting also exists.

**What this settles:** the plan's claude spelling is the documented one and the CLI accepts it without error. On this
account it is held off by a PREFERENCE that outranks the `--settings` value (`"preference"` even with `true`) — an
account, organization or managed setting, or the per-session opt-in. That is the person's configuration, not
something coai may change. So: Off is confirmed (`off`); On is unconfirmed here, and the card must show the run's
reported `fast_mode_state` / `fast_mode_disabled_reason` rather than claim fast mode is on.

**The cause, confirmed by the owner (2026-10-07):** the account is on the Claude Max plan, and the interactive `/fast`
answers *"Fast mode unavailable: Fast mode requires usage credits · /usage-credits to turn them on"*. On a subscription
plan fast mode is billed from usage credits only, outside the plan's own limits, so with credits off every run reports
`off` whatever `--settings` says. Turning credits on is the owner's decision (it is a separate spend); coai's spelling
stands as documented.

## Not measured yet

- How a headless claude run is put into fast mode, if it can be at all.
- An api row's tier (`xai`): no key export available yet (the CredsForDevs entry exports no variable).
- Whether `-c service_tier=default` really overrides a `priority` line in the config: the line on this machine is now
  `default`, so that case would need a temporary config — not done without the owner's go.
