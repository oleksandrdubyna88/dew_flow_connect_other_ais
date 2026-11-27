# The alias `haiku` in plan mode — measured, 2026-11-27

> The record behind PR #1065. Two real calls, one question: in `--permission-mode plan`, which model
> answers when the argv says `--model haiku`, and which when it says `--model claude-haiku-5-5`?

## Setup

- Date: 2026-11-27. claude CLI **2.1.332**.
- Run by the coordinator, with the owner's go, in an empty scratch folder. No key file was read or
  copied by the agent that wrote this record.
- The argv is the one `ClaudeModels` builds for a question consultant row with no tools (no secret
  is in it; the prompt rode stdin and was one short line):

```
claude -p --output-format json --permission-mode plan --tools "" --strict-mcp-config --model <model>
```

- The answering model is read from the JSON result's `modelUsage` key (the CLI's own report of what
  ran), the cost from `total_cost_usd`.

## Results

| `--model` sent | `modelUsage` key (the model that answered) | `total_cost_usd` | `is_error` |
|---|---|---|---|
| `claude-haiku-5-5` | `claude-haiku-5-5` | 0.00102879 | false |
| `haiku` | `claude-sonnet-5-5` | 0.0185949 | false |

Two calls in total, one per row; this is a measurement of one call each, not a rate.

## Conclusion

- The bare alias `haiku` in plan mode ran Sonnet 5.5 and cost about 18 times as much for the same
  tiny prompt (0.0185949 against 0.00102879 USD). This is the behaviour the CLI's own help text
  announces for `haiku` in plan mode, and the one the v2 paid proof of 2026-11-27 showed.
- The full id `claude-haiku-5-5` in plan mode was answered by `claude-haiku-5-5`: the id is not
  remapped, and it is the model the alias runs outside plan mode (the session file of v2 job
  r20261126012559-2183e184, an acceptEdits coder job, has `"model":"claude-haiku-5-5"`).
- So sending the full id (`ClaudeModels.HaikuId`) gives the person the model they chose, at the price
  of that model. The round note added in the same PR (`ClaudeModels.Mismatch`) is what would show the
  same fault for the next alias the CLI maps.
- Not measured: the other aliases (`opusplan` and the rest), other CLI versions, and a reviewer or
  consultant road run end to end (the argv above is the base both roads build their `--model` from).
