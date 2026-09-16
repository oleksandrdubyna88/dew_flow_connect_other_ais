# PLAN — what the corpus plan did not finish

> Status: **plan only, nothing implemented yet.** Scope: the ranking pass's transport, `coai-bugs`'
> deployment, and the one promise no test in this repository can keep.
>
> Extracted from [PLAN_a_corpus_of_real_defects.md](../research/PLAN_a_corpus_of_real_defects.md)
> when that plan was promoted on 2026-09-16. Everything here was named in it rather than ticked, and
> a named gap in a shipped plan reads as done to everybody who was not there.
>
> **The boundary with [PLAN_the_bugs_release_line.md](../research/PLAN_the_bugs_release_line.md), named here
> as well as there** (`planning-docs.md`: a boundary between two plans is written into the OLDER
> document too, because a division legible from one direction only is how the same work gets built
> twice):
>
> | Item | Built by | Order |
> |---|---|---|
> | `bugs-v*` release line, two Linux RIDs, the smoke | the release-line plan | 1st |
> | `deploy/nginx/coai-bugs` and `deploy/bugs/README.md` | the release-line plan | 1st, before the tag |
> | The post-deploy log check, as an automated gate | **stays here** — it needs a deployment to run against | after a host exists |
> | The ranking pass's transport | **stays here** | independent of both |
> | `panelProvider.ts:2991`'s `Math.random()` nonce | **stays here** | independent of both |
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

## 2. ~~`coai-bugs` has no release line and no deploy notes~~ — CLOSED

Built by [PLAN_the_bugs_release_line.md](../research/PLAN_the_bugs_release_line.md): a `bugs-v*` line over two
Linux RIDs whose smoke starts the PUBLISHED binary and waits for `/health`, because that is the
only thing that can see the embedded keyword list arrive; `deploy/nginx/coai-bugs`; and
`deploy/bugs/README.md`, which travels inside the archive rather than beside it.

**What that plan's own round then found**, and worth carrying forward as the reason this was not a
formality: `error_log ... warn` still records a contributor's address, because `limit_req` logs
`client: <address>` at ERROR level — so a vhost with `access_log off` keeps a dated list of exactly
the people it throttled, and a verification recipe that sends one SUCCESSFUL pair never sees it.

## 3. The no-client-IP promise is a deployment obligation

`/ingest` reads no client address and registers no request-logging middleware, and three plan
reviewers were right that this proves nothing: a reverse proxy writes `remote_addr` before the
request reaches any route. `UseSerilogRequestLogging` is deliberately not wired for the same reason.

**Half of it is now closed.** The deploy notes and the vhost exist, and the server watches its own
edge: any forwarding header arriving makes it warn, by NAME and never by value, because it cannot
read an nginx config it has no reliable path to and a check that passed on a file nginx never
loaded would be worse than none.

**What is still open is the AUTOMATED post-deploy check** — running the recipe in
`deploy/bugs/README.md` against a real host and failing if an address appears in any of the four
places. It cannot be written until there is a deployment to run it against, and it belongs with the
other post-deploy checks rather than in a unit suite, because the thing being checked is a
machine's configuration and not this code.

## 4. Smaller, and honest about being small

- `KeyFor` scans every active key row, deliberately, and the note in it says an indexed lookup is
  right if the table ever reaches thousands. Nothing measures the table, so nothing will notice.
- `panelProvider.ts:2991` still builds a CSP nonce with `Math.random()`. SonarCloud flagged the same
  pattern elsewhere and it was fixed there; this one was reported rather than silently rewritten
  because it is outside the corpus work entirely.

## Build order

2 and the first half of 3 are done, in that order and for that reason — the deploy notes are an
input to the release rather than a follow-up. What is left is 1 and 4, which touch nothing each
other or the closed items touch, and the automated post-deploy check, which waits on a host.

## Definition of done

- [ ] The ranking pass calls a local model and orders by its answer, with the fake-model tests above.
- [x] `bugs-v*` releases `coai-bugs`, with a smoke that starts the published binary.
- [x] A deploy notes file records the nginx configuration that keeps the no-IP promise.
- [ ] A post-deploy check reads the deployed logs after a real ingest and finds no address.
- [ ] `panelProvider.ts:2991` uses `randomBytes`, or the operator says it stays.
- [ ] This plan is promoted when the list above is done.
