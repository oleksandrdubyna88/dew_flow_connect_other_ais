# PLAN — one live streamed api call, measured and kept as a fixture

> Status: **plan only, nothing implemented yet (2026-10-07).** The open tail of
> [PLAN_api_streaming.md](../research/PLAN_api_streaming.md) (build step 5), extracted when that plan was promoted.
> Blocked on the owner: no vault entry with a vendor key exports an environment variable yet. Scope: one measurement,
> one redacted fixture, one test; no product code unless the measurement finds a defect.
>
> Related docs: [RESULTS_api_streaming_vendors.md](../research/RESULTS_api_streaming_vendors.md),
> [RESULTS_api_streaming_measurements_2026-10-06.md](../research/RESULTS_api_streaming_measurements_2026-10-06.md).

## Goal

Every streamed answer coai reads today is SYNTHETIC: the endpoint stub's chunks, and the recorded non-streaming goldens
re-told as streams (`AStreamedGoldenIsReadLikeItsRecordingTests`). What a real vendor sends — the order of its usage and
`finish_reason` chunks, its keep-alive comments, how it splits a multi-byte character — is documented
(`RESULTS_api_streaming_vendors.md`) but not observed. One live call turns that into evidence and a fixture the reader is
held to.

## What to do

1. **The owner's go and a key.** A vault entry for xAI or OpenRouter that exports its key as an environment variable
   (CredsForDevs: right-click the entry → Edit → switch on the variable); the Token Plan after 2026-10-25 16:00 UTC.
   The call sends one short test prompt to that vendor — the owner approves it before it is sent.
2. **The call.** `coai-mcp --ask-api --stream on` with the row's dialect and measured model (`src_mcp/core/Api/ApiVendors.cs`
   `Modules`), the raw response body captured beside the usage line and exit.
3. **The fixture.** The raw SSE, with ids, the key and any account detail replaced, saved under
   `src_mcp/tests/fixtures/api-goldens/<vendor>-stream.sse`.
4. **The test.** The fixture served by `ApiEndpointStub` as `text/event-stream`, read through the module, asserting the
   exit, the usage line (with `"streamed":true`) and the answer file — a golden held to, never regenerated silently.
5. **The record.** `research/RESULTS_api_streaming_live_call_<date>.md`: vendor, model, CLI version, what the stream
   looked like against the documented shape, the cost.

## Test plan

- The live fixture read through its module gives exit 0, its usage line and the answer.
- The fixture's usage equals what the vendor reported for the call (the record says how it was compared).

## Definition of Done

- [ ] The owner approved the call; one live streamed call ran and is recorded in `research/`.
- [ ] Its redacted SSE is a fixture with a test that reads it.
- [ ] `research/module_tests.md` names the fixture; this plan promoted.
