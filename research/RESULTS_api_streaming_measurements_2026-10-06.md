# RESULTS — api streaming: what was measured while it was built (2026-10-06)

> Status: **record, 2026-10-06, branch `feat/api-streaming` (PR #689).** Every experiment and measurement taken while
> [PLAN_api_streaming.md](PLAN_api_streaming.md) was built. Vendor documentation (what each API says it does)
> is in [RESULTS_api_streaming_vendors.md](RESULTS_api_streaming_vendors.md); this file holds only what was observed
> on this machine and in CI. No live vendor call has run yet; see *Not measured*.

## 1. The qwen row's failures, from coai's own logs

The owner suspected that the qwen row (Alibaba Model Studio "Token Plan", OpenAI-compatible endpoint) failed because
it never streams. Measured, not assumed:

| Source | `%LOCALAPPDATA%\coai-mcp` logs and `usage.jsonl`, 2026-10-04 → 2026-10-06 |
|---|---|
| Successful qwen-row calls | n = 90, median 149 s, max 424 s |
| HTTP 400 from aliyuncs, or any `stream` / `enable_thinking` error | **none** |

The failures that DID occur, each with its real cause:

| # | What the log says | Cause | Does streaming fix it? |
|---|---|---|---|
| 1 | `429 … Your token-plan 1-month quota has been exhausted. The quota will reset at 10-25 16:00:00 UTC` | quota spent | no — wait for 2026-10-25 16:00 UTC |
| 2 | `did not finish in time - it was still working after 290s of the 290s` (exit 69) | OUR deadline: a 5-minute round timeout minus 10 s (`src_mcp/runners/Reviewers/LocalAsk.cs:42-48`) | no — it is our limit, not the vendor's |
| 3 | `returned no message content (65536 reasoning tokens)` (exit 70, `deepseek-v4-flash-0731`) | reasoning used the whole output ceiling | no |

**Alibaba's documented ~300 s cut-off for non-streaming calls is refuted on this endpoint:** non-streaming calls of
**393 s, 404 s and 424 s** succeeded on the Token Plan host. Streaming is therefore a switch a person may turn on (it
makes stream-only models usable, and keeps bytes flowing past a gateway that cuts silent connections). It is not a
repair for the qwen row.

## 2. Test runs (local, Windows 11, .NET 10, Node 24.18)

| When | Suite | Result |
|---|---|---|
| After Stories A–B | C# `CoaiMcp.Tests.exe` | 7976 pass |
| After Stories A–B | extension `npm test` | 5566 pass, 1 load flake (passed on rerun) |
| After Story C | C# | 7993 total, **0 failed** (7985 pass, 8 skipped) |
| After Story C | extension | 5483 total, **0 failed** (5481 pass, 2 skipped); `npx eslint src` clean |
| After the gate fixes | C# | 7999 total, 7990 pass, **1 failed** — see §4 |
| After the gate fixes | extension | 5483 total, 0 failed |
| After the SonarCloud fix (run alone) | C# | 8000 total, **0 failed** (7992 pass, 8 skipped) — the §4 flake did not recur |
| After the CodeRabbit fix | extension | 5483 total, 0 failed |
| Every stage | `npm run test:seam` | passes, the `apiStreamSeam` leg included |
| Every stage | family checks (plan-lifecycle, pin-check, gate-snippet-check, build-flags-check, adapter-check) | clean |

## 3. Teeth checks: each new test was seen to FAIL without its fix

| Test | Fix removed | Observed failure |
|---|---|---|
| `AStreamedAskTests.The_usage_line_says_streamed_only_when_a_stream_was_read` | `UsageLine(..., streamed: true)` | the usage line had no `"streamed"` |
| `AStreamedRowIsCheckedTests` (real child) | the same flag, in the binary the child runs | the check record had no `streamed` verdict |
| `anApiRowCanStream.test.ts`, badge | `STREAMED` lookup not yet written | the badge said `checked: it answered` only |
| `AStreamIsReadAgainstItsLimitsTests.One_event_of_many_short_lines…` | `SseEvents.PendingChars` | `Ended` instead of `LineTooLong` |
| `AStreamIsReadAgainstItsLimitsTests.The_stream_ends_at_its_DONE…` | stopping at `[DONE]` | `TimedOut` after the 5 s deadline instead of `Ended` |
| `AStreamedAnswerIsReadLikeAnyOtherTests.The_answer_is_the_choice_whose_index_is_0…` | the `index == 0` search | the answer content was `null` |
| `anApiRowCanStream.test.ts`, skew note | the `apiStream` entry of `IGNORED` | the note was missing |

## 4. What CI and the reviewers found

- **coai plan gate** (codex `gpt-6-luna`, 40.7 s, 38 666 tokens in / 3 011 out): `proceed`, 3 findings, all accepted.
  They became Stories C and D.
- **coai code gate** (codex, 4 roles, 292 186 tokens in / 11 007 out): `proceed`, 6 findings. 4 were accepted and
  fixed (an unbounded event, found by two roles; an `as never` fixture; `architecture.md`). 2 were rejected with
  reasons (a new immutable state for each chunk; failing a test on a leftover temp directory).
- **Own reviewer agent**, run at the same time: the reader waited for the body to end after `[DONE]` (fixed), a
  misplaced doc comment, and methods over complexity 4.
- **CodeRabbit**: 1 finding. A skew-note test searched the generated HTML; it now asserts the renderer's decision as
  a value. Fixed, and the thread resolved.
- **SonarCloud**: the reliability gate failed on one BUG, `S1751` at `StreamAssembler.cs:167`. `FirstChoice` looked
  at the first choice only, so choice 0 after choice 1 was lost. It was a real defect: fixed with a RED test first.
  The same run reported 26 smells. 17 of them are `S1135` "TODO" hits, which are false positives: they are
  `todo/PLAN_…` path mentions in comments.
- **CI infrastructure, not a test:** the `extension · typecheck · test · package` job failed once with "The runner has
  received a shutdown signal". It was re-run.
- **A flaky test, not caused by this branch:**
  `ConsultantModesCliScenarioTests.AHolderKilledMidCheck_IsAbandonedToTheNextReader_AndANewCheckRuns` failed once in a
  full run with `IOException: The process cannot access the file '…\.argv'` at `ConsultantModesCliScenarioTests.cs:133`.
  The test reads the fake CLI's argv file while the child still writes it. It passed 3 of 3 times when run alone.
  It ran in parallel with another worktree's test run, so the extra load is the likely trigger.

## 5. The two owed tests, 2026-10-07

Branch `test/api-streaming-tail`, local Windows 11, .NET 10, Node 24.

| What | Result |
|---|---|
| `AStreamedGoldenIsReadLikeItsRecordingTests` (9 cases) + `AStreamsUsageIsNeverZeroTests` (4) + the two touched classes | 46 of 46 passed |
| C# suite, `CoaiMcp.Tests.exe` | 8098 total, 0 failed, 8 skipped, 13 m 11 s |
| Extension: compile, `npx eslint src`, `npm test` | 11 groups, 0 failed |
| Family checks (plan-lifecycle, pin-check, gate-snippet-check, build-flags-check, adapter-check) | all clean |

**Teeth.** Both classes hold code that had already shipped, so neither could start red. Each was broken on purpose
with a change that compiled, run, and restored:

| Mutation | What went red |
|---|---|
| `AskApiMode.StreamedAsync` prints the usage line only for a stream that ENDED | `A_stream_over_the_answer_cap_is_unknown` — exit 70 with no line reads as `Usage.None` (1 of 13) |
| `StreamAssembler` ignores a `usage` object on a chunk with no choice | the parity cases: "the same usage, said to have streamed, but they differ at index 12" |

What the parity cases cover: each recorded golden through each module that answers in its shape — xai (`grok-4.7`)
with the two xAI goldens; qwen (`qwen3.8-max`) and deepseek (`deepseek-v4-pro`) with the DashScope turn and cut; glm
(`glm-5.3`) with the DashScope turn and the reasoning-only answer; openai (`gpt-5`) with its turn. The streams are
synthetic, built from the recording; the live stream is still the open item below.

## Not measured

- **A live streamed call on a real vendor** (plan build step 5). The owner gave the go on 2026-10-06. It waits because
  the CredsForDevs entry "x ai grock api key" exports no environment variable yet ("exports no environment variable.
  Open Edit and switch one on first").
- xAI's per-chunk usage behaviour (the documentation does not say; only a live call will).
- Whether a Token Plan stream-only model works through this switch — after the quota resets on 2026-10-25.
