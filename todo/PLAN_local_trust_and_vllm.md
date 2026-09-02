# PLAN — a local reviewer you can trust, and one that is not Ollama

> Status: **plan only, nothing implemented yet.** Scope: `src_vs_code/src/localEngines.ts`,
> `panelProvider.ts`, `src_mcp/runners/Reviewers/LocalAsk.cs`, `LocalRuntime.cs`.
>
> Extracted from [PLAN_local_models.md](../research/PLAN_local_models.md), whose third gate round
> ended in `call_human` with six findings still gating. The operator decided: two of them were done
> in that change, these four were deferred here. Nothing below is a defect in what shipped — each is
> a limit that is currently written down rather than handled.
>
> Related docs: [module_runners.md](../research/module_runners.md),
> [module_extension.md](../research/module_extension.md).

## Where this came from

The local-model change was reviewed by this product's own gate three times: nine findings, then
seven, then seven, and the rounds ran out. What was fixed in that change: the exfiltration warning,
the empty-schema contradiction, the `Environment.ProcessPath` blocking bug, the probe reasons, the
stale model marking, the WSL sentence, the shim's own deadline and the re-probe control.

What follows is the remainder, in the reviewers' order of severity. They are grouped because they
share one shape: **the local reviewer today assumes the engine is a trusted Ollama on this machine**,
and each of these is a way that assumption is not true.

## 1. A non-loopback endpoint should be acknowledged, not merely announced

**The finding (Major, codex).** "A user who pastes an external vLLM URL, or follows a saved endpoint
a year later after its ownership changes, can submit the plan, diff, and surrounding file contents to
that host based only on a visible line that may be overlooked."

What exists now: `isLoopback` parses the host, and a non-loopback endpoint puts a visible line in the
row naming the host and what is sent. That is a warning, not a decision.

What must be true when this is done:

1. A non-loopback endpoint is not used for a review until somebody has said yes to that specific
   host, once.
2. Changing the endpoint invalidates the acknowledgement — the year-later case in the finding is
   exactly the endpoint staying the same while the host changes hands, so the acknowledgement is
   per-host-string and re-asked whenever it moves.
3. The acknowledgement is stored where a cloned repository cannot supply it. Workspace settings are
   how a hostile endpoint would arrive in the first place, so the yes belongs in extension global
   state, keyed by host.
4. A refusal is visible in the row rather than silent, and the round refuses with a reason rather
   than sending nothing and reporting an empty answer.

## 2. Authentication, for the engines that want it

**The finding (Major, codex).** "A vLLM deployment requiring an API key … can return 401/400 even
though `/v1/models` is otherwise reachable; the user gets a failed or undiscoverable reviewer with no
configured way to authenticate."

There is no key path for a local vendor at all today: the row says "no CLI, no key, no bill", which
is true of Ollama and false of a served vLLM behind a gateway. The other custom vendors take a key
from the CredsForDevs vault entry under their own id, and that mechanism already exists — this is
plumbing it through, plus a probe that distinguishes 401 from unreachable so the row can say "it is
there and it wants a key" rather than "no engine answered".

## 3. Streaming, so cancellation is not Ollama-specific

**The finding (Major, antigravity).** "OpenAI-compatible servers like vLLM, LM Studio, or setups
behind reverse proxies do not abort in-flight non-streaming `/v1/chat/completions` generations when
the client drops the TCP connection before headers are sent, leaving GPU resources occupied."

Measured for Ollama: killing the shim mid-generation dropped GPU compute to 0% within six seconds,
because closing the socket is a client disconnect it acts on. **Not measured for anything else**, and
the reviewer's mechanism is plausible: a non-streaming handler that has not yet written a byte may
not notice.

The proposed fix is `stream: true`, so the server is writing into a pipe that breaks. It is a real
change rather than a flag: the shim would assemble the answer from deltas, and `usage` arrives
differently (or not at all) in a streamed response — which is the token count this feature just
established. So: measure first against a real vLLM, then decide, and keep the non-streaming path if
the disconnect turns out to be handled.

## 4. The two tests the gate asked for by name

**The finding (Minor, codex).** The endpoint race and the trust warning are promised behaviour with
no test naming them.

- **The race**: endpoint A probes slowly, the field changes to B, A's answer arrives last. The code
  discards it by comparing the current configuration after the await; nothing asserts that.
- **The warning**: a test that a non-loopback endpoint cannot reach a review without the row having
  said so — which only becomes meaningful once §1 exists.

## 5. A reasoning model's thinking cannot be bounded through the OpenAI route — RESOLVED 2026-09-02

**Resolved the day it was written, by the answer `dew_flow_rag_qln` had already found**
(`AiRuntimeOptions.ReasoningEffort`, measured 2026-08-11): `reasoning_effort: "none"` is honoured on
Ollama's OpenAI route where `think:false`, `chat_template_kwargs` and `"low"` are not. Shipped in
mcp 0.11.2 as the local reviewer's default (`COAI_LOCAL_REASONING_EFFORT`, `engine` to send nothing).
The native-route idea below is therefore NOT needed and is kept only as the record of what was
considered. What remains open from this section is the panel exposing the setting; today it is
env-only.

*As originally written:*

**Measured 2026-09-02.** Gemma4 26B on Ollama 0.33.2 answered the planted-defect plan once in 171 s
and, on the identical request, once filled a 64k context with 110 000 characters of `reasoning` and
returned an empty `content` after 1056 s. `think: false` and `reasoning_effort: low` on `/v1` are
silently ignored — the two requests produce byte-identical answers. Ollama's native `/api/chat`
honours `think`, and vLLM does not serve it.

So the shim needs an engine-aware path: when the endpoint is Ollama (the probe already knows —
`api/version` answered), send the completion through `/api/chat` with `think` bounded or off, and
keep `/v1` for everything else. Whether a review WITHOUT thinking finds as much is the measurement
to run first; the one with thinking finds four of eight when it finishes and nothing when it does
not.

## 6. A prompt longer than `num_ctx` is truncated without a word

Ollama cuts the prompt to the model's context and reviews what is left, reporting it as a review.
The hosted code-stage prompt is ~205k tokens against a 64k model, so a local code round would
silently review the first third of the diff. The shim can detect it: `usage.prompt_tokens` coming
back far below a tokens-per-character estimate of what was sent is truncation, and the round must
refuse with the sizes rather than return findings about a fragment.

## Build order

1. §4's race test first, against today's code — it is a test for behaviour that already exists and
   it will be the regression guard for §1's changes.
2. §1, because it is the one with a security consequence.
3. §2, which is mostly wiring an existing vault path.
4. §3 last, and only after a measurement against a real non-Ollama engine.

## Definition of Done

- [ ] A non-loopback endpoint cannot be reviewed against until acknowledged for that host, and the
      acknowledgement does not survive the host changing.
- [ ] A 401 from an engine reads as "it wants a key", not as "nothing answered".
- [ ] The streaming question is answered by a measurement against a non-Ollama engine, and the answer
      is recorded whichever way it goes.
- [ ] The endpoint race has a test that was watched fail.
- [x] Thinking is off by default through `reasoning_effort: "none"` on the OpenAI route itself — no
      native route needed. The with/without difference on the planted-defect plan is in
      `RESULTS_model_comparison.md`.
- [ ] The panel exposes `COAI_LOCAL_REASONING_EFFORT` beside the local row; it is env-only today.
- [ ] A prompt truncated to `num_ctx` refuses the round with the two sizes, never reviews a fragment.
