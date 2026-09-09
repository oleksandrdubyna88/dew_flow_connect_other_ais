# PLAN — the Team server should know a chat from a review, and outlive the client that asked

> Status: **IMPLEMENTED, 2026-09-09.** All three shipped in Team server 0.5.6 and extension 0.31.17:
> `kind` as a contract with `review` as the default, abandonment on two clocks, and an idempotency
> key bound to a fingerprint of the request. The panel's spending block separates conversations from
> rounds, which closes the owner's *"счиатть, отделять"* — the one item the chat plan had to leave open.
>
> **Deviations, all from the plan round of this product's own gate.** (1) The abandonment window is
> TWO numbers, not one: three minutes for a queued job, ten for a running one. Treating them alike was
> called blocking and rightly — a queued job has cost nothing, a running one has already been billed
> for whatever it has done, so killing it for a network blip destroys work somebody paid for.
> (2) The idempotency key carries a FINGERPRINT of the request; a key repeated with a different
> question is a 409 rather than the first job's answer, which would otherwise have answered a question
> nobody asked while looking exactly like success. (3) A poll JUDGES before it stamps, so a client
> that vanished for five minutes cannot resurrect a job the sweep was entitled to drop.
>
> Scope: `src_server` and the shared job contract; the extension half shipped first, deliberately.
>
> Related docs: [PLAN_chat_with_other_ais.md](../research/PLAN_chat_with_other_ais.md) (phase 5 is what raised
> all of this), [../research/module_extension.md](../research/module_extension.md).

## Why this exists

The chat can now ask a Team server a question, and it works — measured on 2026-09-09 against
`coai.remsoft.dev`, a real answer in 5.2 seconds. Three things about it are held together with
something better than nothing, and each one is a server change rather than a client one.

## 1. A chat is not a review role, and it is not "no role" either

The first version sent `role: "Chat"` and the server refused it in 0.2 seconds:

> `'Chat' is not a review role. Allowed: PlanCritique, Conventions, Architecture, SecurityReliability, UxDxPerformance`

**The server is right.** A role there is not a label: it carries a shipped prompt, a threshold and a
round budget, and `RolePromptsTests` walks every value of that enum asserting each one asks for an
honest empty findings list. `Chat` in that enum would be a reviewer that cannot review.

What the server DOES accept is a job with **no role at all** — `ReviewEndpoints.cs:145` skips the
enum check when the role is blank — and the extension uses that today. It even gives the owner what
he asked for, because a usage row with no role is a conversation and every review carries one. But
it is a validation gap being read as a contract, and the day somebody tightens that check every chat
on every machine stops working with a message about roles.

**What to build:** an explicit `kind` on the job — `review` (the default, so every existing client
keeps working) or `chat`. The role stays required for a review and absent for a chat, which is what
already happens; the difference is that it becomes a promise rather than an accident.

**The CLIENT half is already done, deliberately ahead of the server.** `CHAT_KIND` ships in every
chat body from extension 0.31.14 (`remoteAsk.ts`). It had to go first: the day the server requires a
role for a job it reads as a review — which is what `review` being the default MEANS — a client
sending a blank role and no `kind` is refused, and that client is every copy already installed. One
sending `kind: 'chat'` is not. Raised by codex on the code round of phase 5, against this plan's own
claim that an old client keeps working, and it was right.

Safe today, and measured rather than assumed: `coai.remsoft.dev` accepted a body carrying `kind` in
56 ms on 2026-09-09, exactly as it accepted one without — an unknown property is ignored, which is
System.Text.Json's default and one `UnmappedMemberHandling.Disallow` away from not being. So the
server change is now purely additive: read the field, default it to `review`, and the fleet is
already sending it.

Then `UsageReader` can group by it, and the panel's spending section can show conversations beside
rounds rather than instead of them — which is the half of the owner's *"счиатть, отделять"* that
this repository cannot honour on its own.

## 2. A job whose client is gone should expire

The extension cancels a queued review when its tab closes, and that is best-effort by construction:
no promise survives an extension host being killed. A job nobody will ever poll then holds a vendor
slot — the scarcest thing on a shared account — until it runs and answers into nothing.

**What to build:** a job that has not been polled for N minutes expires. The server already has
`ExpiryReason` for a queued job; this is the same idea, keyed on the last poll rather than on age.

## 3. A submit whose response was lost creates a second job

If the POST succeeds and the answer never arrives — a network that dropped, a proxy that timed out —
the person presses send again and the server has two jobs for one question, on an account where a
slot is the scarce thing.

**What to build:** an idempotency key the client generates per turn, which the server keys the job
on. A repeat of the same key returns the first job rather than making another.

## What this plan is NOT

It is not a new endpoint and not a new queue: a chat turn is a job, and everything about how it is
claimed, run, priced and recorded stays exactly as it is. Three fields and one expiry rule.

## Test plan

Server-side: a job with `kind: chat` and no role is accepted; one with `kind: review` and no role is
refused; an old client that sends neither is accepted as a review; usage rows carry the kind; a
second submit with a used idempotency key returns the first job; a job nobody polls expires and frees
its slot. Extension-side: the client sends `kind` and a key, and a server that does not know either
still answers — because a server older than the client is the ordinary state of a fleet.

## What the live server said

Three measurements against `coai.remsoft.dev`, each before the thing it justified was trusted.

**A chat turn carries no review role** (2026-09-09, before the extension half shipped). The first
version sent `role: "Chat"` and the server refused it in 0.2 s, naming the five real roles. What it
accepted was a job with no role at all — which is the accident this plan exists to replace with a
promise.

**An unknown property is ignored, so the client could ship first** (2026-09-09, 56 ms). A body
carrying `kind` was accepted exactly as one without it. That is what made it safe for extension
0.31.15 to start sending a field no server had yet heard of — and it had to be measured rather than
assumed, because System.Text.Json ignoring unknown members is a DEFAULT, one
`UnmappedMemberHandling.Disallow` away from not being.

**The whole new body against the OLD server** (2026-09-09, after this work was released and before
the VM was updated). This is the measurement that matters to a person today, because the client half
is deployed by an extension update and the server half by a person on a VM, and the two are never in
step:

```
live server: {"ok":true,"version":"0.5.5"}
sending fields: vendor, model, prompt, role, kind, timeoutSeconds, idempotencyKey
key shape: 7f20bd66-f953-48ca-974d-d61a32298826 (36 chars)
accepted 202 in 158ms
turn took 3.5s
answer: OK
```

A server that knows neither new field takes both and answers normally. **The deploy is the remaining
step and it is deliberately a person's**: `deploy-server.yml` is `workflow_dispatch` only, because
putting a new binary on the box everybody's reviews run through is a decision rather than a
consequence of a tag.

## Definition of Done

- [x] `kind` is part of the job contract, and NULLABLE rather than defaulted — which is the whole
      design: it is the only way to tell an old client that said nothing from a new one that said
      `review`, and the first must keep working. Six rows of a table, six tests.
- [x] The extension sends it, and still works against a server that ignores it. *(0.31.15, measured
      against the live server before the server knew the field: accepted in 56 ms, exactly as a body
      without it.)*
- [x] `UsageReader` carries it and the panel's spending section separates conversations from rounds —
      one line under the vendor rows, folded through the SAME arithmetic those rows use, and silent
      when there is only one kind, because an absence is not a zero.
- [x] A job nobody polls expires and frees its vendor slot — its PROCESS stopped, not merely its
      record marked, and on two clocks: three minutes queued, ten minutes running.
- [x] A repeated submit with one idempotency key produces one job; the same key for a different
      question is a 409 rather than somebody else's answer.
- [x] Measured against the live server, as phase 5 was — see *What the live server said* below.
