# PLAN — a prompt has a ceiling, and a finished job forgets it

> Status: **DECLINED by the operator, 2026-09-11 — not built, and the reason is on the record.**
> The arithmetic stands: no bound on a prompt but nginx's 4 MB, a cancelled job keeping its prompt
> for an hour, and 120 requests per 10 s per email, which reaches `MemoryMax=1500M` in about half a
> minute. What it needs is an authorised caller doing it — every caller here is a colleague, and the
> realistic version is a retry loop in our own client rather than an attacker. Weighed against a
> change to the wire contract (a 413 every installed extension would have to meet) and a transport
> limit on every endpoint, the operator chose not to.
> **What re-opens it:** the server reachable by anyone outside this team, or one OOM restart nobody
> can explain. Scope: `src_server/src/Jobs/ReviewEndpoints.cs`, `JobStore.cs`, `Program.cs`,
> `http/reviews/errors.http`. Finding 2 of
> [the product audit of 2026-09-09](../research/REVIEW_product_audit_2026-09-09.md).
>
> Related docs: [module_team_server.md](../research/module_team_server.md) — story 2.3,
> [architecture.md](../research/architecture.md) — *How the Team server is deployed* (`MemoryMax=1500M`).

## The symptom, with the arithmetic

`ReviewEndpoints.cs:192-197` refuses an EMPTY prompt and nothing else; the only bound on a prompt's size
is nginx's `client_max_body_size 4m` (`deploy/nginx/coai:51`). `JobStore.cs:124` caps how many of one
person's jobs may be QUEUED (20). A cancelled job (`JobStore.cs:294`) and an expired one
(`JobStore.cs:461`) keep their whole record — prompt included — until the sweep forgets terminal records
after `_keep`, one hour (`JobStore.cs:43`). The rate limiter is 120 requests per 10 s per EMAIL
(`Program.cs:95-111`).

So one authorised client — or one client with a bug in its retry loop — can hold:

```
6 submit+cancel pairs / s  ×  4 MB per prompt  ×  2 (UTF-16 in a .NET string)  =  48 MB / s
1500 MB (MemoryMax)  ÷  48 MB/s  ≈  30 s
```

about half a minute to the unit's memory ceiling, never once exceeding the queued cap. The kernel then
kills the server, systemd restarts it (`Restart=on-failure`), every job in memory is gone — a poll
answers `lost` — and the loop can start again. The audit reproduced the retention: forty 1 MiB prompts
submitted and cancelled against a queue cap of 20, and forty records still holding 40 MiB of prompt
after `Sweep`.

Nothing the product sends is anywhere near that. A shaped diff is at most `DiffShaper.DefaultMaxBytes`,
192 KiB (`src_mcp/core/Context/DiffShaper.cs:28`); a plan round's prompt is about 33 KB
(`AntigravityRuntime.cs`); a chat turn carries a transcript bounded at 60 000 characters. One mebibyte is
a ceiling with more than twice the headroom of the largest legitimate prompt, and it is a ceiling the
sender is TOLD about rather than one nginx enforces with a bare 413.

## The change

1. **`Coai:MaxPromptBytes`**, default `1_048_576`, read in `Program.cs` beside the other `Coai:*` keys
   and handed to the review endpoints. `Refusal` gains the size check: a prompt over the ceiling is
   refused with **413** and a sentence naming the ceiling and the size that arrived — the family's
   *fail naming the legal values*, not a bare status. Counted as UTF-8 bytes of `request.Prompt`, which
   is what travelled on the wire.
2. **A terminal record keeps no prompt.** Every path that writes a terminal state — `Finish`
   (`JobStore.cs:273`), `Cancel` (`:294`), `Expire` (`:461`) — stores the record `with { Prompt = "" }`.
   Nothing observable changes: `ReviewStatusDto` (`ServerJsonContext.cs:88-97`) never carried the
   prompt, and a retry with an idempotency key compares the FINGERPRINT hashed at submit
   (`ReviewEndpoints.cs:140-145`), not the text. The runner's own copy of the record lives for the
   length of one launch and no longer.
3. `http/reviews/errors.http` gains the 413 request, its body built by a script block
   (`'x'.repeat(1_048_577)`) rather than a megabyte of literal text in a file.
4. `module_team_server.md`: the key in *Configuration*, and a story 2.3 bullet.

### The growth budget, after the change

Per caller: at most 20 queued + 3 running prompts of 1 MiB → **46 MiB** of UTF-16 at the absolute
worst; a terminal record without its prompt is on the order of a kilobyte plus the vendor's answer,
kept one hour so the client can collect it. A cancel loop at the rate limit therefore accumulates
~22 000 answer-less records an hour, about **20 MB**, and the sweep retires them. A company-wide byte
budget on top of these was considered and left as a tail: at ten people the worst case is a third of
the ceiling and needs every one of them to be doing the worst thing at once; past thirty people it is
worth a second look, and this section is where the number to compare against lives.

### What the gate's plan round changed (2026-09-10, accepted)

- **The answer has a ceiling too** (codex, Major). Removing the prompt leaves the vendor's ANSWER on every
  terminal record for the retention hour, and the launcher's own new ceiling permits 16 MiB of it. The
  attack shape is different — an answer needs a real vendor run, so it is minutes and an account rather
  than a request — but the bound costs nothing: `Coai:MaxAnswerBytes`, default 1 MiB, applied where the
  runner records a success, with the truncation NAMED in the stored text so a client cannot read a cut
  answer as a whole one. Test 6.
- **The size is refused BEFORE the body is bound** (gemini, Major). Checking `request.Prompt` after
  deserialization means the megabyte was already read, allocated and parsed — the protection arrives one
  allocation too late, which is precisely the failure mode under a flood. So `Kestrel`'s
  `MaxRequestBodySize` is set to `MaxPromptBytes` plus a 64 KiB envelope allowance, and an endpoint filter
  refuses on `Content-Length` before model binding. The post-binding check stays as the exact,
  field-level answer (a chunked request has no `Content-Length`), and its 413 is what names the two
  numbers. Test 7 asserts a body over the transport ceiling is refused without the handler running.
- **`ci.yml` is a step, not only a checkbox** (codex, Minor). The DoD asked for the workflow change and
  the plan never made it one. It is step 5 now: `CoaiServer.Tests` runs in the pull-request job beside
  `CoaiMcp.Tests`, and `adapter-check`-style workflow assertions are not needed here because the test
  project's absence from CI is visible in the same file.

## Test plan

| # | Test | Holds |
|---|---|---|
| 1 | `ReviewEndpointTests.APromptOverTheCeilingIsRefusedWith413NamingTheLimit` — over the wire, a 1 MiB + 1 prompt; the body names both numbers | RED today: 202 |
| 2 | `ReviewEndpointTests.APromptAtTheCeilingIsAccepted` | the bound is inclusive and the ceiling is not off by one |
| 3 | `JobStoreTests.AFinishedCancelledOrExpiredJobKeepsNoPrompt` — the audit's shape: forty submit+cancel pairs of 1 MiB against a cap of 20; the sum of stored prompt lengths after `Sweep` is zero | RED today: 40 MiB |
| 4 | `IdempotencyTests` — a retry of a job that already FINISHED still answers with that job | the fingerprint, not the prompt, is what a retry needs; must stay green after step 2 |
| 5 | `http/reviews/errors.http` — `a_prompt_over_the_ceiling_is_refused_naming_it` | the wire tier, run by `npm run test:contract` |
| 6 | `JobRunnerTests.AnAnswerPastItsCeilingIsStoredTruncatedAndSaysSo` | the second retained field is bounded, and a cut answer cannot read as a whole one |
| 7 | `ReviewEndpointTests.ABodyOverTheTransportCeilingIsRefusedBeforeTheHandlerRuns` | the refusal arrives before the allocation |

## Definition of Done

- [ ] Tests 1–7 written, watched fail for the real symptom, passing; whole server suite and the contract suite green. (6 and 7 were added by the gate's plan round and belong here: a plan reopened later must not be markable complete with the answer ceiling and the pre-handler refusal undone. CodeRabbit, on the pull request.)
- [ ] The key is documented; the growth budget above is in `module_team_server.md`.
- [ ] `ci.yml` runs `CoaiServer.Tests` on pull requests — today only `release.yml` and `sonarcloud.yml` do, so a red server test reaches `main` unseen.
