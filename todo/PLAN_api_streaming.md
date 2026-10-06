# PLAN — api rows can stream their answer (`"stream": true`), switched per row on the new Settings page

> Status: **in progress, 2026-10-06 — build steps 1–4 and the docs built on `feat/api-streaming`; the live call, two tests
> of the test plan and the promotion open (see Progress).** Revised the same day after an own plan review. Scope:
> coai-mcp's `--ask-api` path, one new catalog row field, and one control on the new Settings page's model card.
>
> Related docs: [RESULTS_api_streaming_vendors.md](../research/RESULTS_api_streaming_vendors.md) (what each vendor
> does), [PLAN_one_model_catalog.md](PLAN_one_model_catalog.md) (the catalog and its new page),
> [PLAN_local_trust_and_vllm.md](PLAN_local_trust_and_vllm.md) §3 (streaming for `--ask-local`, not this plan).

## Goal, and what the evidence says

The owner asked (2026-10-06) to find which api vendors can stream and to let a row switch it on where it is supported,
suspecting the qwen row's trouble comes from never streaming. Every api call is non-streaming today: `ChatRequest.Body`
writes `"stream": false` unconditionally (`src_mcp/core/Api/ChatRequest.cs:58`) and the reply is read whole
(`src_mcp/src/Api/BoundedBody.cs:21-43`, then `CompletionReader.cs:46-151`). No SSE code exists anywhere.

**Streaming fixes none of the qwen failures the logs show.** `%LOCALAPPDATA%\coai-mcp` logs and `usage.jsonl`,
2026-10-04..06 (n=90 successful qwen-row calls, median 149 s, max 424 s), hold no HTTP 400 from aliyuncs and no
`stream`/`enable_thinking` error. The failures were:

1. `429 … Your token-plan 1-month quota has been exhausted. The quota will reset at 10-25 16:00:00 UTC`;
2. `did not finish in time - it was still working after 290s of the 290s` (exit 69) — OUR deadline (a 5-minute round
   timeout minus 10 s, `src_mcp/runners/Reviewers/LocalAsk.cs:42-48`), not the vendor's;
3. `returned no message content (65536 reasoning tokens)` (exit 70, `deepseek-v4-flash-0731`) — reasoning used the
   whole ceiling.

Alibaba documents a ~300 s cut-off for non-streaming calls, but on the Token Plan host non-streaming calls of 393, 404
and 424 s succeeded — **documented, refuted on this endpoint**. What a stream does buy: the models Alibaba serves ONLY
as a stream (open-source Qwen3, QwQ, some thinking modes — `parameter.enable_thinking must be set to false for
non-streaming calls`) become usable, and a gateway that cuts a silent connection sees bytes flowing. This is a switch a
person may turn on, not a qwen repair, and the card's "?" says so.

## Decisions

| # | Decision | Why |
|---|---|---|
| S1 | **A row setting, `stream` (boolean; absent = off = today).** Not a dialect field. | Whether a stream works depends on the endpoint AND the model; dialect fields are probe-measured and shared (`shared/api-dialects.json` `$comment`). Off keeps every row as it is. |
| S2 | **Measured by the row's own ✓ Check, not by the probe.** With the switch on, Check runs a real streamed turn through the same `ApiRuntime` path and its badge records the outcome. The switch is offered on every api row as unmeasured until then. | The extension runs `--probe-api` only without `--model` (`apiModelsProbe.ts:29`) — the variant matrix never runs from the panel and its report has nowhere to reach the card. Check already has a durable record and a badge on the card (`healthBadge(facts.check)`), and measures THIS row, model and endpoint. |
| S3 | **On the wire only when the binary says so:** `stream` crosses in `COAI_VENDORS` only when `coai-mcp --features` lists `apiStream`, by its own `streamOnTheWire` beside `promptOnTheWire`/`timeoutOnTheWire` (`vendorsWire.ts:112-139`); parsed in `catalogFields.ts` like `timeoutField` — for api rows, `true` only. **Not** an api setting in `apiSettings.ts`. | `apiSettings.ts` gates by VERSION (`API_SETTINGS_SINCE`, `apiSettings.ts:26, 152-154, 203-207`) and `storedApiSettings` is also called by `vendors.ts:389`: a field there would reach any binary ≥ 0.40.0 past the feature gate. |
| S4 | **One answer shape.** The stream is assembled into the completion object the non-streaming path reads, and `CompletionReader` reads it unchanged. | A streamed and an unstreamed answer cannot be judged differently. |
| S5 | **A stream that began with 200 always reports its usage first, then its exit.** The usage line is printed before the exit is decided — the last usage seen, or `notCaptured`, never 0. Then: `[DONE]` or a final `finish_reason` → the answer as today; an `error` chunk, or a `finish_reason` of `error`/`network_error`/`sensitive`/`insufficient_system_resource`/`aborted` → exit 75 when its text carries a rate-limit phrase or a 429/503 code (written so `RateLimit.Hit` reads it, `ReviewerExecutor.cs:248`), else 70; a stream that ends without `[DONE]` and without a `finish_reason`, or drops after the headers (`IOException`) → 69 (`EndedBeforeAnAnswerExit`), "the stream ended before the answer finished (N characters of answer, M of reasoning)". | `CompletionReader.Classify` reads every 200 as `Answered` (`CompletionReader.cs:185-191`), so a mid-stream error must be decided here. A run that ended without a usage line records `Usage.None` (`ApiRuntime.cs:147-151, 174`) — zero for a paid generation — so the line comes first. A retry on 75 is a new, fully billed generation; that is the existing ladder's rule and is kept. |
| S6 | **New UI only.** The switch is drawn by the new Settings page's model card (`modelCard.ts`) for api rows — not by `apiSettingsFields`, which the current page also draws. A binary without `apiStream` gets the card's existing skew note (`catalogShell.ts:28-36`, an `IGNORED` entry in `modelCard.ts:118-128`), silent while the feature list is unsettled. A "new" tag (`newTags.ts` `NEW_CONTROLS`) and a help key. | The owner's rule of 2026-10-06, and the card's own conventions for a field a binary ignores. |
| S7 | **Out of scope:** the probe's variant matrix (S2); `--ask-local` (PLAN_local_trust_and_vllm §3); the chat (no api runtime); the Team server (refuses `api` rows); an idle timeout separate from the deadline. | One path changed and measured at a time. |

## Design

**coai-mcp (C#).**

- `core/Api/SseReader.cs` (new, pure): lines in → `data` payloads out, by the SSE rules: blank lines and `:` comments
  skipped (`: keep-alive`, `: OPENROUTER PROCESSING`); `event:`/`id:`/`retry:` lines skipped (an error rides `data:`);
  `data:` with an optional space; several `data:` lines joined; `data: [DONE]` ends; `\r\n` tolerated.
- `core/Api/StreamAssembler.cs` (new, pure): payloads in → one completion JSON built with `Utf8JsonWriter` (the binary
  is Native AOT): `content` joined from `choices[index 0].delta.content`; reasoning COUNTED from
  `delta.reasoning_content` or `delta.reasoning` and kept only as a bounded quote for a note — never stored whole, never
  in content; the last non-null `usage` wherever it came (cumulative assumed; xAI's per-chunk usage unconfirmed until a
  live call); the last non-null `finish_reason`, an error chunk or error reason anywhere winning over a later `stop`
  (which also settles OpenRouter's doubled `finish_reason`); the outcomes of S5.
- `src/Api/StreamedBody.cs` (new, beside `BoundedBody`): reads the response lines with the deadline's token on every
  read, decodes UTF-8 as a stream (a character split across network chunks survives), caps ONE line (a line with no
  newline is refused) and caps the ASSEMBLED answer at the existing 8 MiB — not the raw SSE, which is several times the
  answer (≈ 280 bytes of envelope per chunk; a 65 536-token answer is ~19 MB of SSE).
- `ApiTurn` gains `Stream` (beside `ThinkingOn`, `core/Api/ApiTurn.cs:11-18`); every module in `ApiVendors.All` keeps it
  through its `RequestBody`, the thinking-off branches included; `OpenAiCompatibleTransport` passes it to
  `ChatRequest.Body` as a new parameter defaulting to `false`, which writes `"stream": true` and, right after it,
  `"stream_options": {"include_usage": true}` — and is byte-identical to today when off (`LocalAsk.cs:214` and the
  goldens untouched).
- `AskApiMode`: `--stream on` (a key with a value — `Program.cs:1821-1834` `Flags` drops a bare flag; the precedent is
  `--thinking off`) → `turn.Stream`; the response branch is chosen by the response's Content-Type, not by the flag — a
  `200 application/json` to a streamed request (a gateway that ignored `stream`) is read whole as today. Notes quote the
  assembled answer or the error chunk, not raw SSE. A timeout's 69 text says how much answer and reasoning had arrived
  (whether it was thinking or writing — the diagnostic failure (2) lacked).
- The row field end to end: `VendorDto` → `PanelSettings.ParseVendors` → `ApiRowSettings`/`ApiEffective` →
  `ReviewerSettings.Stream`, set by ONE helper at all three places an api row becomes a reviewer:
  `RosterBuilder.cs:397-403`, `ConsultantTurnInputs.cs` (also the path of ✓ Check, `ConsultantCheck.cs:203`) and
  `QuestionFanOut.cs:311`; `ApiRuntime.Build` adds `--stream on`; `FeaturesMode` lists `apiStream`.

**Extension (TS).**

- `vendors.ts` `stream?: boolean`, parsed by `catalogFields.ts` (api rows, `true` only; a saved `false` reads as absent);
  `vendorsWire.ts` `streamOnTheWire(v, features)`; `binaryFeatures.ts` knows `apiStream`.
- `modelCard.ts`: for an api row, a **"Stream the answer"** switch (`data-setting="stream"`, written by the existing
  `vendor` write path), its "?" text (help.ts) saying what it buys and what it does not, a "new" tag, and the skew note
  when the binary lacks `apiStream`. Not drawn by `apiSettingsFields` or anywhere on the current page.

## Risks, said

- `stream_options` is undocumented at Z.ai; the calibrated GLM and DeepSeek modules go through DashScope
  (`GlmVendor.cs:53`, `DeepSeekVendor.cs:45-48`), so only a generic row aimed at z.ai is exposed. It is always sent
  (Alibaba, OpenAI and Ollama return no usage without it); a backend that refuses it shows that in the row's ✓ Check.
- A killed reviewer drops the connection, which usually stops the vendor's generation — expected, not measured.
- A row whose `executablePath` points at an older coai-mcp ignores `--stream on` silently (`ApiRuntime.cs:73`); rare.

## Build order

1. `SseReader` + `StreamAssembler`, pure, with their tests.
2. `ApiTurn.Stream` through every module and `ChatRequest`; `StreamedBody`; the `AskApiMode` stream branch with S5's
   usage-and-exit rules; `ApiEndpointStub` (`src_mcp/tests/ApiEndpointStub.cs`) taught to serve `text/event-stream` in
   chunks.
3. The row field on the server, the three `ReviewerSettings` places, argv, `--features apiStream`.
4. Extension: field, wire, the model card switch.
5. A live streamed call on a vendor whose key is in the vault (xAI or OpenRouter now; the Token Plan after
   2026-10-25 16:00 UTC), its raw SSE redacted and kept as a golden fixture.
6. Docs: `research/module_core.md` (core/Api), `module_server.md` (src/Api), `module_runners.md` (ApiRuntime),
   `module_extension.md` (the card); this plan promoted.

## Test plan

- **SSE reader:** comments, blank lines, `event:`/`id:` skipped; `data:` with and without the space; multi-line data;
  `[DONE]` ends and nothing after it is read; CRLF.
- **Assembler:** content joined; reasoning counted from `reasoning_content` and from `reasoning`, never in content, not
  stored whole; usage from a last `choices: []` chunk, from every chunk, and with `finish_reason`; an error chunk after a
  `stop` wins; each error `finish_reason`; `finish_reason` with no `[DONE]` and no usage → the answer kept, usage
  `notCaptured`; nothing final → broken; `length` → today's cut exit.
- **Parity (S4):** one recorded non-streaming golden per module re-told as a stream (synthetic, said so) gives the SAME
  usage line and exit; plus the live fixture of build step 5.
- **Body:** off → goldens byte-identical; on → exactly `stream: true` then `stream_options`; every module keeps
  `turn.Stream`, thinking-off branches included.
- **Ask path, against the stub:** a streamed answer reaches `--out`; a mid-stream rate-limit error exits 75 and
  `RateLimit.Hit` reads its note; another error exits 70; a cut stream exits 69 with the arrived counts; a `200
  application/json` answer to a streamed request is read whole; a long stream under the assembled cap passes and an
  unterminated giant line is refused; a UTF-8 character split across chunks survives.
- **Usage, never 0:** `ApiRuntime`'s `ReadUsage` gives the counted usage or Unknown — never `Usage.None` — for a cut
  stream, an error chunk, the cap and the deadline.
- **Flags and settings:** `Flags` sees `--stream on`; argv has it only when the row says so; all three
  `ReviewerSettings` places carry it; `--features` lists `apiStream`.
- **Wire:** `stream` crosses only with `apiStream`; a saved `false` crosses as nothing.
- **Model card (page tree, the page's own script):** an api row draws the switch and posts `stream`; a CLI row does not;
  the skew note appears under a settled list without `apiStream` and not under an unsettled one; the "new" tag and the
  help key exist; the current Settings page draws no stream control.

## Definition of Done

- [ ] Every test above written first (RED), then green; the extension suite, lint, the seam, the C# suite and the
      family checks pass.
- [ ] A streamed and an unstreamed answer are judged by one reader, shown by the parity tests.
- [ ] The switch is on the new page's model card only.
- [ ] One live streamed call measured and kept as a fixture; the plan says which vendor and which model.
- [ ] The four `research/module_*.md` files updated; this plan promoted with `IMPLEMENTED <date>` and its deviations.

## Progress

- **Steps 1–2 built 2026-10-06** (`core/Api/SseEvents.cs`, `core/Api/StreamAssembler.cs`, `src/Api/StreamedBody.cs`;
  `ApiTurn.Stream`, `ChatRequest.Body(..., streamed)`, the `--ask-api --stream on` branch). One deviation from S5: a
  failure reported inside a 200 stream exits **70**, not 75 — the retry ladder decides from the quoted vendor text
  (`RateLimit.Hit` reads stderr at any non-zero exit), so a separate code bought nothing; the note says "inside its
  HTTP 200" and quotes the error. The stub (`ApiEndpointStub`) serves chunked `text/event-stream` and can drop the
  connection mid-answer.
- **Steps 3–4 and the docs, 2026-10-06:** the row field end to end on the server (`VendorDto` → `ApiRowSettings` →
  `ApiEffective` → `ReviewerSettings.Stream` through ONE helper, `WithApi`, at all three places an api row becomes a
  launch; `--stream on`; `--features apiStream`), the extension (`catalogFields`, `streamOnTheWire`, the model card's
  switch with its help, "new" tag and skew note), a seam leg (a row's stream reaches a binary that lists `apiStream`,
  never one that does not), and the four module docs. Open: build step 5, a live streamed call (needs the owner's go —
  it sends a test prompt to a vendor), then promotion.
- **The plan gate ran late.** No gate reviewer was available while this was planned (the only one, qwen, had spent its
  Token Plan quota); two own reviewer agents stood in. The coai gate is run over plan and code once it is available.
- **Not built yet, from the test plan:** the per-module parity tests (each module's recorded non-streaming golden
  re-told as a stream, giving the same usage line and exit) — S4 is held today by the assembler tests and one end-to-end
  ask test; and the `ApiRuntime.ReadUsage`-level "never 0" test — today the usage line is checked at the shim's own
  output (`AStreamedAskTests`), which is what `ReadUsage` parses. `SseReader` shipped as `SseEvents`.
