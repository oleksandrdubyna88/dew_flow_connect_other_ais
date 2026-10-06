# PLAN — api rows can stream their answer (`"stream": true`), switched per row on the new Settings page

> Status: **plan only, nothing implemented yet, 2026-10-06.** Scope: coai-mcp's api request path (`--ask-api`,
> `--probe-api`), one new catalog row field, and one control on the new Settings page's model card.
>
> Related docs: [RESULTS_api_streaming_vendors.md](../research/RESULTS_api_streaming_vendors.md) (what each vendor
> does), [PLAN_one_model_catalog.md](PLAN_one_model_catalog.md) (the catalog and its new page),
> [PLAN_local_trust_and_vllm.md](PLAN_local_trust_and_vllm.md) §3 (streaming for `--ask-local`, not this plan).

## Goal, and what the evidence says

The owner asked (2026-10-06) to find which api vendors can stream and to let a row switch it on where it is supported,
suspecting that the qwen row's trouble comes from never streaming. Every api call today is non-streaming:
`ChatRequest.Body` writes `"stream": false` unconditionally (`src_mcp/core/Api/ChatRequest.cs:58`) and the reply is
read whole (`src_mcp/src/Api/BoundedBody.cs:21-43`, then `CompletionReader.cs:46-151`). No SSE code exists anywhere.

**The logs do not show a stream refusal.** `%LOCALAPPDATA%\coai-mcp` logs and `usage.jsonl` for 2026-10-04..06 (n=90
successful qwen-row calls, median 149 s, max 424 s) hold no HTTP 400 from aliyuncs and no `stream`/`enable_thinking`
error. The qwen rows failed for three other reasons:

1. `429 … Your token-plan 1-month quota has been exhausted. The quota will reset at 10-25 16:00:00 UTC` — not this plan.
2. `did not finish in time - it was still working after 290s of the 290s` (exit 69): our own deadline (a 5-minute round
   timeout minus 10 s, `LocalAsk.cs:42-48`) reached while the model was still reasoning.
3. `returned no message content (65536 reasoning tokens)` (exit 70, `deepseek-v4-flash-0731`): reasoning used the whole
   ceiling — not this plan.

**What streaming does buy** (vendor docs, the RESULTS record): Alibaba ends a NON-streaming call at about 300 s
(`500 RequestTimeOut`) while a stream keeps going, so a long qwen answer under a 10-minute round can finish; models that
answer only in a stream (open-source Qwen3, QwQ, some thinking modes: `parameter.enable_thinking must be set to false
for non-streaming calls`) become usable; and gateways that cut an idle connection see bytes flowing. It does not make a
model faster, and it does not fix (1) or (3). This is said on the card, not hidden.

## Decisions

| # | Decision | Why |
|---|---|---|
| S1 | **A row setting, `stream` (boolean; absent = off = today).** Not a dialect field. | Whether a stream works depends on the endpoint AND the model (Alibaba requires it for some models, allows it for others); dialect fields are probe-measured and shared by many models (`shared/api-dialects.json` `$comment`). Off by default keeps every row as it is today. |
| S2 | **The probe measures it.** `--probe-api` gains a `stream` variant (the minimal body with `stream: true`); the report says `streams: yes | refused (<reason>) | not measured`. | The plan's rule for api settings: what a vendor accepts comes from a measurement, never from documentation alone. Every documented dialect supports a stream, so the switch is offered on every api row; a refusal the probe saw is drawn beside it. |
| S3 | **On the wire only when the binary says so:** `stream` crosses in `COAI_VENDORS` only when `coai-mcp --features` lists `apiStream`. | The E2.2 precedent (`vendorsWire.ts` `promptOnTheWire`/`timeoutOnTheWire`): an older binary must not be handed a field it would ignore while the card says it is on. |
| S4 | **One answer shape.** The stream is assembled into the same completion object the non-streaming path reads, and `CompletionReader` reads it unchanged — content, `finish_reason`, usage, the reasoning-only and cut cases, the exit codes. | One reader, so a streamed and an unstreamed answer cannot be judged differently. |
| S5 | **An error inside a 200 stream is a failure, classified as one.** A chunk with a top-level `error`, or a `finish_reason` of `error`/`network_error`/`sensitive`/`insufficient_system_resource`/`aborted`, is read as a failed reply carrying that text (so the rate-limit and key phrases still classify it); a body that ends without `[DONE]` and without a `finish_reason` is a broken stream (exit 70, "the stream ended before the answer finished"). | Vendors report mid-stream errors this way (OpenRouter, Z.ai, DeepSeek); a truncated stream must never be read as an answer. |
| S6 | **New UI only.** The switch is drawn by the new Settings page's model card (`modelCard.ts`) for api rows — not by `apiSettingsFields`, which the current page also draws. | The owner's rule of 2026-10-06: new controls go on the new page only. |
| S7 | **Out of scope:** `--ask-local` (its own plan, PLAN_local_trust_and_vllm §3), the chat (no api runtime), the Team server (refuses `api` rows), an idle timeout separate from the deadline (a follow-up once streams are measured). | Keeps one path changed and measured at a time. |

## Design

**coai-mcp (C#).**

- `core/Api/SseReader.cs` (new, pure): lines in → `data` payloads out, by the SSE rules: blank lines and `:` comments
  skipped (`: keep-alive`, `: OPENROUTER PROCESSING`), `data:` with an optional space, several `data:` lines joined,
  `data: [DONE]` ends; tolerant of `\r\n`.
- `core/Api/StreamAssembler.cs` (new, pure): payloads in → one completion JSON: `content` from `delta.content`,
  reasoning from `delta.reasoning_content` OR `delta.reasoning` (kept apart, never in content), the last non-null
  `usage` wherever it came, the last `finish_reason`, `model`/`id` when present; an `error` chunk or an error
  `finish_reason` → a failure (S5); no `[DONE]` and no `finish_reason` → broken.
- `ChatRequest.Body(turn, dialect, stream)`: `"stream": true` and `"stream_options": {"include_usage": true}` when
  streaming; byte-identical to today when not (the goldens pin it).
- `AskApiMode`: `--stream` → the streaming body, then read the response lines (the same 8 MiB ceiling over the raw
  bytes, the same deadline), assemble, and hand the assembled body to `CompletionReader`. A non-200 response is read
  whole, as today.
- `ApiRuntime.Build`: `--stream` in argv when the row's effective setting is on. `ApiRowSettings`/`ApiEffective` gain
  `Stream` (row only; no env override); `PanelSettings.ParseVendors` reads it; `VendorDto` gains it;
  `FeaturesMode` lists `apiStream`; the panel report (`ApiRowReport`) carries it.
- `ProbeApiMode`: a `stream` variant in `Variants`, `stream` in `KnownFields`; the report's capabilities gain
  `streams` (`yes` / the refusal text / absent = not measured).

**Extension (TS).**

- `vendors.ts`: `stream?: boolean`, parsed only when `true`; `apiSettings.ts` gains it as a row setting beside
  effort/thinking/reviewMinutes; `vendorsWire.ts` sends it only under `apiStream` (S3); `binaryFeatures.ts` knows
  `apiStream`.
- `modelCard.ts`: for an api row, a **"Stream the answer"** switch (`data-setting="stream"`), with a "?" text that says
  what it buys and what it does not (from the evidence above), the probe's `streams` result beside it, and — when the
  binary lacks `apiStream` — drawn disabled with "update coai-mcp". Not drawn on the current page.

## Build order

1. `SseReader` + `StreamAssembler`, pure, with their tests.
2. `ChatRequest` stream body (goldens unchanged when off) + `AskApiMode --stream` read path; extend the test endpoint
   stub (`src_mcp/tests/ApiEndpointStub.cs`) to serve `text/event-stream` in chunks.
3. Row field end to end on the server: `VendorDto` → `PanelSettings` → `ApiEffective` → `ApiRuntime` argv →
   `--features apiStream`.
4. Probe variant + report field.
5. Extension: row field, wire gate, the model card switch.
6. Docs: `research/module_*.md` for the api path and the model card; this plan promoted when done.

## Test plan

- **SSE reader:** comments and blank lines skipped; `data:` with and without the space; multi-line data joined;
  `[DONE]` ends; CRLF; nothing after `[DONE]` read.
- **Assembler:** content joined across chunks; reasoning from `reasoning_content` and from `reasoning`, never in
  content; usage taken from the last chunk with `choices: []` (OpenAI/Alibaba/Ollama), from every chunk (xAI), from a
  chunk that also carries `finish_reason` (Z.ai); an `error` chunk and each error `finish_reason` → failure with the
  text; no `[DONE]` and no `finish_reason` → broken; `finish_reason: length` → the same exit as today's cut answer.
- **Parity:** each recorded non-streaming golden response re-told as a stream gives the SAME usage line and exit code
  through `CompletionReader` (the S4 promise, as a test).
- **Request body:** `stream: false` goldens byte-identical; `stream: true` adds exactly `stream: true` and
  `stream_options.include_usage`.
- **Ask path, against the stub:** a streamed answer is written to `--out`; a mid-stream error classifies as a rate
  limit when its text says so; a stream cut before `[DONE]` exits 70; the 8 MiB ceiling holds for a stream.
- **Wire:** `stream` crosses only with `apiStream`; argv has `--stream` only when the row says so.
- **Probe:** the `stream` variant is in the matrix; the report carries `streams`; a refused stream is reported with its
  text.
- **Model card (page tree, the page's own script):** an api row draws the switch and posts `stream`; a CLI row does not
  draw it; without `apiStream` it is disabled with the reason; the current Settings page draws no stream control.
- **Not measured yet, said in the docs:** a live streamed call to each vendor (the Token Plan quota resets
  2026-10-25 16:00 UTC; xAI and OpenRouter keys are in the vault).

## Definition of Done

- [ ] Every test above, written first (RED), then green; the whole extension suite, lint, the seam, the C# suite and
      the family checks pass.
- [ ] A streamed and an unstreamed answer are judged by one reader (`CompletionReader`), shown by the parity test.
- [ ] The switch is on the new page's model card only; the current page is unchanged.
- [ ] `research/module_*.md` updated; this plan promoted with `IMPLEMENTED <date>` and its deviations.
- [ ] A live streamed call measured on at least one vendor before the plan says "measured".
