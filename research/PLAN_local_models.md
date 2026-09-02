# PLAN — a local model as a third reviewer

> Status: **IMPLEMENTED, 2026-09-02.** Scope: `src_vs_code/src/localEngines.ts` (new),
> `vendors.ts`, `models.ts`, `panelView.ts`, `panelProvider.ts`; `src_mcp/runners/Reviewers/`
> (`LocalRuntime`, `LocalAsk`) and the `--ask-local` mode on `coai-mcp`.
>
> Related docs: [module_extension.md](module_extension.md), [module_runners.md](module_runners.md).
> The tail this plan did not close is
> [PLAN_local_trust_and_vllm.md](../todo/PLAN_local_trust_and_vllm.md).

## What shipped, and where it deviated

Everything above shipped as written — the row, the engine discovery, the shim, the direct call, the
tokens, the null cost. Four deviations, all of them from the gate rather than from second thoughts:

1. **The shim's own deadline is derived, not passed.** The plan had the reviewer timeout reaching the
   shim; the reviewers pointed out that a shim killed at exactly the deadline produces silence rather
   than a sentence, so `ShimDeadlineSeconds` takes ten seconds off the reviewer's budget and the
   difference is spent saying what happened.
2. **`Environment.ProcessPath` is not always this program.** It is `dotnet.exe` in a
   framework-dependent run, so the shim would have launched `dotnet --ask-local`. Found by the gate,
   not by the tests — which had only ruled out codex, agy and claude. `SelfInvocation` now carries
   the dll ahead of its own flags.
3. **There is no empty-schema fallback.** The draft fell back to `{}` when the schema would not
   parse, which contradicts this plan's own reason for refusing `json_object`: a request that cannot
   constrain the shape buys a full generation and an unusable answer. It exits 65 before the request.
4. **The row has a re-probe button.** Left out as “a CLI's control” until a reviewer observed that a
   cache with no way to clear it is a stale list with no way out.

The gate ran three times over this change — nine findings, then seven, then seven — and ended in
`call_human`. The operator took those four and deferred the remaining four to
[PLAN_local_trust_and_vllm.md](../todo/PLAN_local_trust_and_vllm.md): acknowledging a non-loopback
host, a key path for a served vLLM, streaming so cancellation is not Ollama-specific, and the two
tests the reviewers asked for by name.

## The goal

Adding a local model puts a THIRD reviewer row in the panel, named `local`, with a dropdown of the
models this machine actually has — the way the `codex` row lists what the Codex CLI cached and the
`gemini` row lists what `agy models` reports. Plus a field for an endpoint URL somebody types
themselves, for a vLLM or a remote box the probe cannot find.

It is a separate runtime. **Not routed through the Codex CLI**: that was tried, and the measurement
below is why it is not the answer.

## What was measured before any of this was designed

Every one of these is a live result from this machine on 2026-09-02, not an assumption.

| question | answer |
|---|---|
| is anything local running? | Ollama on `127.0.0.1:11434`, 13 models; nothing on 8000 / 8080 / 1234 / 5000 |
| does `/v1/models` list them? | yes — so ONE probe shape covers Ollama and vLLM alike |
| where do parameter size, quantisation and disk size come from? | Ollama's native `api/tags` (`7.6B · Q6_K · 6.3 GB`) |
| can a local model answer IN the finding schema? | **yes** — `response_format: {type: "json_schema"}` on `/v1/chat/completions` returned three well-formed findings |
| does the weaker `json_object` mode do? | **no** — it answered with an invented shape. Unusable for a gate |
| Ollama's native `format: <schema>` on `/api/chat`? | also works, but Ollama-only. `/v1` is chosen for portability |
| sampling | `temperature` and `seed` must be sent IN the request — `dew_flow_rag_qln` recorded that Ollama's `/v1` route substitutes its own defaults over the Modelfile |
| reachable from WSL? | **no.** `netstat` shows `127.0.0.1:11434 LISTENING` — bound to the Windows loopback. From WSL the gateway times out and the local loopback refuses |
| routing it through the Codex CLI? | reached the endpoint and answered `LOCAL_OK`, but codex's own system prompt is **21k tokens** before any review content, and an 8k-context model is refused outright. A direct call pays none of that |

## Design

**A shim, not a widened interface.** `IReviewerRuntime.Build` returns a `ProcessRequest`; the
executor starts a process and reads its output. A direct HTTP adapter does not fit that shape, and
widening it reaches `BoundedScheduler`, the concurrency accounting, the usage parser and the failure
classification.

So `coai-mcp --ask-local` is the "CLI": it reads the prompt, POSTs to the endpoint with the schema,
and prints the answer where the executor already looks. The local runtime then IS a process like
every other reviewer, and nothing in the pipeline changes.

This is not a shortcut. The process boundary buys exactly what a local inference call needs most —
a hard timeout and a guaranteed kill — because a hung local generation is ordinary rather than
exceptional. The cost is one process per call, which is what every other reviewer already is.

**Informed by `dew_flow_rag_qln`, with no code crossing between them.** Those are separate
products with no shared library here, so this is written fresh in this repository. What is taken is
what that project LEARNED — how it probes, and that sampling must be explicit:

- The probe URL and the OpenAI base are **different URLs** and conflating them produces a 404 at the
  first completion that reads like a model problem.
- `api/tags` (installed) not `api/ps` (resident in VRAM) — nothing is loaded until something asks, so
  a picker built on `ps` shows an empty list on a machine full of models.
- Unreachable is an **answer with a reason**, never an empty dropdown.

**Three buttons the local row must NOT have.** ▶, ⤓ and ⟳ are a CLI's — run it, install it, update
it. There is no CLI here, so they are hidden rather than shown dead. What the row shows instead is
the engine and its model count.

## Build order

1. `localEngines.ts` — pure: parse `/v1/models`, parse `api/tags`, merge them, and one fetch that
   probes a list of candidate bases. Failure produces a reason, not an exception.
2. `vendors.ts` — a `local` runtime and its preset; `models.ts` — the model list for it comes from
   the discovered engine rather than a shipped table.
3. `panelView.ts` — the row: model dropdown, engine hint, endpoint field, no CLI buttons.
4. `panelProvider.ts` — probe on repaint, only when a local reviewer exists. The TYPED endpoint is
   what gets probed when the field is filled; the candidate list is the fallback for an empty field,
   and the same endpoint travels to the server. The cache is keyed BY endpoint and a probe that found
   nothing is not cached at all, so starting an engine after opening the panel is noticed on the next
   repaint rather than after a TTL.
5. `coai-mcp --ask-local` + `LocalRuntime` on the server side.

## What the gate raised on this plan

Reviewed by this product's own gate before implementation, and it earned its round. Nine findings,
seven gating; seven accepted and two rejected with reasons.

**The one that mattered: a typed endpoint can send your source code anywhere.** The field is
advertised for "a box on the network", so a URL can be pasted — or arrive in workspace settings from
a cloned repository — and every review POSTs the prompt there: the plan, the diffs, the file contents
around them. Nothing anywhere said so. Now `isLoopback` decides by PARSING the host (so
`http://127.0.0.1.evil.test` is not this machine, and the whole 127.0.0.0/8 block is), and a
non-loopback endpoint puts a visible line in the row naming the host and what leaves with it.

Also accepted and fixed: the failure contract of the shim is now tested rather than described (a 500,
a truncated body, prose instead of the schema, an unreachable host, and tokens that survive an
unusable answer); the probe cache is keyed by endpoint so a slow answer for one cannot populate
another's row; a probe that found nothing is not cached; and a selected model the engine no longer
lists is MARKED rather than shown as current — dropping it would silently switch reviewer, and
showing it plainly would let a round be sent for a model that 404s.

Rejected, with reasons recorded in the session: the claim that the typed endpoint is ignored (it is
not — it is probed first and passed through, with a test), and the claim that `/api/tags` returning
404 would drop models on a vLLM (it is pure enrichment over `/v1/models`, with a test asserting a
model present only in the portable list survives).

**One fix the reviewers proposed and I did not take**: falling back to `json_object` when an engine
rejects `json_schema`. Measured against the real endpoint, `json_object` answers with an invented
shape — so the fallback would turn a clear 400 into a round of unusable findings. The 400 is
reported with the engine's own message instead.

## Test plan

RED first for each: the two parses over real captured payloads; the merge preferring `api/tags`
detail; a probe with nothing listening producing a named reason; the WSL case producing the
`OLLAMA_HOST` sentence rather than silence; the row having no ▶/⤓/⟳; the model dropdown listing what
the probe found; and the shim's request body carrying the schema, the temperature and the seed.

## Definition of Done

- [ ] Every rule above holds with a test that was watched fail.
- [ ] A machine with nothing local shows the row's reason, not an empty list.
- [ ] The shim's answer is read by the existing executor with no change to it.
- [ ] `research/module_runners.md` records why the shim exists rather than a widened interface.
- [ ] The GPU question below is answered before a local vendor can run a CODE round.

## Settled by the operator: no GPU arbitration

The open question was whether a local code round should coordinate with `dew_flow_rag_qln` over the
card. **Answered: it should not.** The two products work independently, so there is no lease, no
back-off and no plan-only restriction — a local vendor reviews plans and code like any other, and
whoever asks for the card gets it.

That removes the only reason the two products would have needed a channel between them, which is
worth more than the contention it avoids.

## Tokens and cost

**Tokens are counted, and that was not a given.** Measured against the real endpoint: Ollama's
`/v1/chat/completions` answers with `usage: {prompt_tokens, completion_tokens, total_tokens}`, so a
local round appears in the spending chart with real numbers rather than a dash.

**Cost is a TODO, deliberately left as one.** A model on your own hardware has no token bill; what it
costs is electricity and the card being busy, and this product can see neither. So `costUsd` is
null — never 0, because free and unpriced are different facts and the chart already keeps them apart
for codex and antigravity. If a figure is ever wanted it has to come from somewhere real: a rate per
hour of card time, or a measured watt-hour, not a token price invented to fill a column.
