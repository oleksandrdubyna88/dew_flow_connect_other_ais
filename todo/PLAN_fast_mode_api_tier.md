# PLAN — fast mode for api rows, and a codex version floor

> Status: **plan only, nothing implemented yet (2026-10-07).** The open tail of
> [PLAN_fast_mode.md](../research/PLAN_fast_mode.md), extracted when that plan was promoted. Blocked on the owner for
> part 1: no vault entry with an xAI key exports an environment variable yet. Scope: `shared/api-dialects.json`, the
> `--ask-api` shim (`ApiRuntime` argv, `AskApiMode` parse, `ChatRequest.Body`), the model card for an api row; and one
> codex measurement.
>
> Related docs: [RESULTS_fast_mode_vendors.md](../research/RESULTS_fast_mode_vendors.md),
> [RESULTS_fast_mode_measured_2026-10-07.md](../research/RESULTS_fast_mode_measured_2026-10-07.md).

## Goal

Fast mode shipped for codex and claude rows (PR #693). Two things it planned were left, each for want of a measurement:

1. **An api row's tier.** The plan's design is a `fastTier` value per dialect in `shared/api-dialects.json` — written
   only from a recorded call, per that file's "measured-only" rule — reaching the shim as `--service-tier <value>` (as
   `--stream on` does) and the request body through `ChatRequest.Body`. `xai` first. Nothing was written, because no
   call was made: the key is not exported.
2. **A codex version floor.** codex 0.160.0 reads `-c service_tier` and DROPS a value its model does not advertise,
   with a warning. How an OLDER codex treats the key was never measured. If one refuses it, Off — sent on every codex
   launch by default — would break that launch.

## What to do

1. **Measure an older codex** (the oldest the panel still accepts) with `-c service_tier=default` and `=fast`: accepted,
   ignored or refused. Record it in `research/`. Only if one refuses: a floor in `shared/feature-availability.json`'s
   codex row, the flag sent only at or above it, and a `VendorDiagnosis` pattern naming the refusal (RED tests first).
2. **The owner's go and a key** for the api part: an xAI vault entry that exports its key; one short call per state,
   approved before it is sent.
3. **The xai value**, measured: the request member and value for each state, and what the answer reports. Recorded.
4. **The tier through the shim**: `fastTier` in `shared/api-dialects.json` (both halves read it), the shim argument, the
   body member, the card's control for an api row on a dialect with a value — tests first, the seam's fast-tier leg
   extended to api rows.

## Test plan

- An older codex: the recorded answer for each state; if a floor ships, a launch below it carries no tier.
- The api tier: the body golden for Off and On on `xai`; no member on a dialect without a value; the card draws the
  control only for such a row; the seam's both-halves leg agrees on api rows.

## Definition of Done

- [ ] The older-codex measurement recorded; a floor shipped only if a refusal was seen.
- [ ] The owner approved the xAI calls; the measured value recorded in `research/`.
- [ ] The api tier end to end with tests written first; module docs updated; this plan promoted.
