# RESULTS — which codex releases accept `-c service_tier`, 2026-10-07

> Status: **record, 2026-10-07.** The measurement the fast-mode plan left open
> ([PLAN_fast_mode.md](PLAN_fast_mode.md), *What shipped differently*: "how an OLDER codex treats `-c service_tier` was
> not measured"). It found a refusal, so it is also the evidence for the fix that follows it.

## Why it was measured

Since PR #693 (released in coai-mcp **0.44.0**), every codex launch whose row is Off — the default — carries
`-c service_tier=default`, so a person's own `service_tier = "priority"` cannot make reviews fast unasked. Story 0
measured codex **0.160.0** only: it reads the key, and DROPS a value its model does not advertise, with a warning. No
older release was asked.

## Method

Windows 11, the person's own ChatGPT login and `~/.codex/config.toml` (model `gpt-6.1-sol`, `service_tier = "default"`).
Each release run through `npx -y @openai/codex@<version>` in an empty folder:
`codex exec --json --skip-git-repo-check -c service_tier=<value> "Reply with the single word OK."`
The raw stdout and stderr of every run were kept; only the config-load line and any tier warning were read.

**Every run failed AFTER config load for another reason, which does not affect the result:** these releases cannot use
`gpt-6.1-sol` with a ChatGPT account (`400 … The 'gpt-6.1-sol' model is not supported when using Codex with a ChatGPT
account`; 0.100.0 and 0.130.0 also failed to parse the models list, `unknown variant max`). The question here is the
step BEFORE that: does the release accept the flag at all.

## Result

| codex | `service_tier=default` | `service_tier=fast` | `service_tier=bogus` |
|---|---|---|---|
| 0.100.0 | accepted (the key is not known — ignored) | accepted | accepted |
| 0.105.0, 0.106.0, 0.107.0 | accepted | — | — |
| **0.110.0, 0.115.0, 0.120.0, 0.125.0, 0.130.0** | **refused at config load:** `Error loading config.toml: unknown variant \`default\`, expected \`fast\` or \`flex\` in \`service_tier\``, exit 1, no request made | accepted (0.130.0) | refused the same way (0.130.0) |
| 0.131.0, 0.132.0, 0.133.0, 0.134.0, 0.135.0, 0.140.0, 0.145.0 | accepted | — | — |
| 0.150.0 | accepted | accepted, with *"Configured service tier `priority` is not advertised as supported for model `gpt-6.1-sol` and will be omitted from requests"* — it maps `fast` to `priority` | dropped with the same warning |
| 0.160.0 (Story 0) | accepted | accepted | dropped with a warning |

No patch release exists between the edges: npm lists `0.107.0`, `0.110.0` and `0.130.0` with no `.1`.

## What this settles

- **codex 0.110.0 through 0.130.0 cannot be told Off.** Their `service_tier` takes only `fast` or `flex`; `default`
  fails the whole launch before any request. coai-mcp 0.44.0 therefore breaks EVERY codex review, consultation and
  question row on those releases — the default row state is the one that fails.
- `fast` is accepted on every release measured, so On does not break a launch (a release that does not advertise it
  drops it with a warning, or maps it).
- Releases before 0.110.0 ignore the key; 0.131.0 and later accept `default`.

## What it does not settle

- Whether 0.110.0–0.130.0 with `fast` actually run fast — no request got through with this account's model.
- `flex` on those releases (not a state coai has).
