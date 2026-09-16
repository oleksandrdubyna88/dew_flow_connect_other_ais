# PLAN — what the corpus plan did not finish

> Status: **plan only, nothing implemented yet.** Scope: the ranking pass's transport, `coai-bugs`'
> deployment, and the one promise no test in this repository can keep.
>
> Extracted from [PLAN_a_corpus_of_real_defects.md](../research/PLAN_a_corpus_of_real_defects.md)
> when that plan was promoted on 2026-09-16. Everything here was named in it rather than ticked, and
> a named gap in a shipped plan reads as done to everybody who was not there.
>
> Related docs: [module_server.md](../research/module_server.md),
> [module_tests.md](../research/module_tests.md),
> [architecture.md](../research/architecture.md).

## 1. The ranking pass has no transport

`Ranking.Order(offered, answered)` exists, is covered, and decides the order of a reply that nothing
produces. Story 5 shipped the ordering and left the call: `RankingModels` enforces the local-only
allowlist at the collection boundary, and no code asks a model anything.

**The symptom if this stays open:** the collector offers every candidate in git order for ever, and
the allowlist is a rule about a call nobody makes — which is the kind of code that gets deleted as
dead by somebody who is right about the evidence and wrong about the intent.

**Where it goes:** `src_mcp/core/Collecting/Ranking.cs:1` holds the ordering; the caller belongs
beside `CollectRun`, and it must go through the same local-model path the allowlist already names.

**Test plan:** a fake local model answers a subset, an invented id, and a reordering; the collector's
order follows `Order`'s rules in each case. One scenario over a real local engine, skipped when none
is configured.

## 2. `coai-bugs` has no release line and no deploy notes

The binary builds, the tests run it, and nothing ships it. `coai-mcp` and the extension have release
workflows; this has neither a tag shape nor a published artefact.

**What the release needs**, from what story 6 already proved it needs:

- A tag shape of its own — `bugs-v0.1.0` — because its version is not the MCP server's and a shared
  tag would release two things on one decision.
- `COAI_BUGS_CONTRACT_EXE` pointed at the published binary in the workflow, exactly as
  `COAI_CONTRACT_EXE` already is for `coai-mcp`. `TheBuiltBinariesTests` is written to be that smoke
  and is worth nothing to a release that does not run it against the published artefact. **This is
  the one that matters most:** the defect it exists to catch is a keyword file that is present in a
  checkout and absent from a deployment, and only the published binary can prove otherwise.
- A deploy notes file recording the nginx and Kestrel configuration — see 3.

## 3. The no-client-IP promise is a deployment obligation

`/ingest` reads no client address and registers no request-logging middleware, and three plan
reviewers were right that this proves nothing: a reverse proxy writes `remote_addr` before the
request reaches any route. `UseSerilogRequestLogging` is deliberately not wired for the same reason.

**What closes it:** a deploy notes file naming the nginx directives that suppress the access log for
this vhost and the Kestrel settings that keep it, plus a post-deploy check that reads the deployed
stack's logs after a real ingest and asserts no address appears. The check belongs with the other
post-deploy checks, not in a unit suite, because the thing being checked is a machine's
configuration and not this code.

## 4. Smaller, and honest about being small

- `KeyFor` scans every active key row, deliberately, and the note in it says an indexed lookup is
  right if the table ever reaches thousands. Nothing measures the table, so nothing will notice.
- `panelProvider.ts:2991` still builds a CSP nonce with `Math.random()`. SonarCloud flagged the same
  pattern elsewhere and it was fixed there; this one was reported rather than silently rewritten
  because it is outside the corpus work entirely.

## Build order

3 before 2 — the deploy notes are an input to the release, not a follow-up — then 1, which touches
nothing either of them touches. 4 is independent of all three.

## Definition of done

- [ ] The ranking pass calls a local model and orders by its answer, with the fake-model tests above.
- [ ] `bugs-v*` releases `coai-bugs`, and the workflow runs `TheBuiltBinariesTests` against the
      PUBLISHED binary through `COAI_BUGS_CONTRACT_EXE`.
- [ ] A deploy notes file records the nginx and Kestrel configuration that keeps the no-IP promise.
- [ ] A post-deploy check reads the deployed logs after a real ingest and finds no address.
- [ ] `panelProvider.ts:2991` uses `randomBytes`, or the operator says it stays.
- [ ] This plan is promoted when the list above is done.
