# RESULTS — streaming (`"stream": true`, SSE) across the OpenAI-compatible APIs coai's api rows call

> Status: **research record, 2026-10-06.** Vendor documentation read on 2026-10-06; only Alibaba's pages carry a date
> (last updated 2026-09-28). Input to `todo/PLAN_api_streaming.md`. Nothing here is measured on a live call yet.

## Why this was looked at

The owner reported trouble with the qwen row (Alibaba Model Studio "Token Plan", OpenAI-compatible endpoint
`https://token-plan.ap-southeast-1.maas.aliyuncs.com/compatible-mode/v1`) and suspected that coai never asks for a
stream. coai's api rows send non-streaming `/chat/completions` requests today.

## Findings per vendor

| Vendor | Stream | Where it is REQUIRED | Usage in a stream | Reasoning in `delta` | Format notes |
|---|---|---|---|---|---|
| Alibaba DashScope / Token Plan | yes (default `false`) | **yes**: open-source Qwen3, QwQ, QVQ, Omni models; any `enable_thinking: true` without a stream → HTTP 400 | only with `stream_options.include_usage`; the last chunk, `choices: []` | `reasoning_content` | `data: [DONE]`; a non-streaming call times out at ≥ 300 s (`500 RequestTimeOut`) |
| xAI (Grok) | yes (Chat Completions is a legacy endpoint) | no | in every chunk | `reasoning_content` in the example; the reasoning page says Chat Completions carries none — unconfirmed | `data: [DONE]`; their examples set a 3600 s client timeout |
| DeepSeek | yes | no | with `include_usage`: `null` in every chunk but the last | `reasoning_content` | `: keep-alive` comments in a stream, blank lines before a non-streaming JSON; closes if inference has not started in 10 min; `finish_reason` `insufficient_system_resource` / `aborted` |
| Z.ai / Zhipu GLM | yes | only for `tool_stream` | always in the last chunk; `stream_options` undocumented | `reasoning_content` | `finish_reason` `network_error` / `sensitive` / `model_context_window_exceeded` |
| OpenRouter | yes | no | always, in the last event (the request flags are deprecated no-ops) | `reasoning` + `reasoning_details` | `: OPENROUTER PROCESSING` comments; an error mid-stream arrives with HTTP 200 as a chunk with a top-level `error` and `finish_reason: "error"` |
| Ollama (`/v1`) | yes | no | with `include_usage`: a separate chunk, `choices: []`, plus `timings` | `reasoning` (not `reasoning_content`) | errors only before the stream starts (from the source) |
| OpenAI | yes | no | with `include_usage`: a separate chunk, `choices: []` | none (a `reasoning_tokens` count only) | `obfuscation` field; `event: error` |

### Alibaba, the case that matters

Error texts from the error-code page (HTTP 400, `InvalidParameter`):

- `parameter.enable_thinking must be set to false for non-streaming calls`
- `This model only support stream mode, please enable the stream parameter`
- the deep-thinking page: `parameter.enable_thinking only support stream call`; "Some models (such as qwen3-235b-a22b,
  qwen3-32b and other open-source versions) only support Stream output".

`enable_thinking` defaults: **on** for Qwen3.5 and newer (3.6, 3.7, 3.8); off for qwen-plus, qwen-flash, qwen-turbo,
qwen3-max. Some thinking models accept only `true`. So a bare non-streaming call to a Qwen3.5+ model risks the first
error (an inference from those two facts; no page says it in one sentence). The Token Plan pages say nothing of their
own about stream or thinking; the model rules are assumed to apply.

Sources: alibabacloud.com/help/en/model-studio/ — `error-code`, `deep-thinking`, `stream`,
`qwen-api-via-openai-chat-completions`, `compatibility-of-openai-with-dashscope`, `token-plan-quickstart`.
Others: docs.x.ai (streaming, legacy chat-completions, reasoning); api-docs.deepseek.com (create-chat-completion,
rate_limit, thinking_mode, news260424 — `deepseek-chat`/`deepseek-reasoner` retired after 2026-07-24); docs.z.ai
(streaming, stream-tool, chat-completion); openrouter.ai/docs (streaming, usage-accounting, reasoning-tokens);
docs.ollama.com/api/openai-compatibility and ollama `openai/openai.go`; the official openai-python SDK types
(platform.openai.com answered 403/404).

## What an SSE client must do (from the above)

1. Read headers first (`HttpCompletionOption.ResponseHeadersRead`), then lines from the body stream; no whole-response
   buffering. Keep the overall budget on a `CancellationToken` and an IDLE timeout between lines, reset by any line —
   a keep-alive comment included.
2. Parse SSE: skip blank lines and lines starting with `:`; take `data:` (optional space after the colon); `data: [DONE]`
   ends normally. A body that closes without `[DONE]` and without a `finish_reason` is a broken stream, not an answer.
3. An error can arrive INSIDE a 200 stream: a chunk with a top-level `error`, or `finish_reason` `error` (OpenRouter),
   `network_error`/`sensitive` (Z.ai), `insufficient_system_resource`/`aborted` (DeepSeek).
4. Usage: keep the last non-null `usage` wherever it appears; a chunk with `choices: []` is normal. Send
   `stream_options: {"include_usage": true}` (unconfirmed that every strict backend accepts it; undocumented at Z.ai).
5. Reasoning: collect `delta.reasoning_content` OR `delta.reasoning`, into its own buffer, never into `content`.
6. DashScope: a stream is effectively REQUIRED for thinking models; without one, `enable_thinking: false` must be sent.
7. A DeepSeek non-streaming body may start with blank lines — never test "first byte is `{`".

## Not confirmed by a source

Token Plan's own stream/thinking rules and its region (one page says Singapore, a Chinese-site snippet Beijing); which
xAI models return `reasoning_content` in Chat Completions; Z.ai's `stream_options`, mid-stream error format and
timeouts; Ollama's behaviour on an error after the stream started; OpenAI's "an interrupted stream may lose the usage
chunk" (search snippet only).
