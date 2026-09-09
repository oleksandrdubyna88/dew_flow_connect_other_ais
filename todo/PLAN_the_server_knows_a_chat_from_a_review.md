# PLAN — the Team server should know a chat from a review, and outlive the client that asked

> Status: **plan only, nothing implemented yet.** Scope: `src_server` and the shared job contract;
> the extension half already ships and works around what is missing here.
>
> Related docs: [PLAN_chat_with_other_ais.md](PLAN_chat_with_other_ais.md) (phase 5 is what raised
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

## Definition of Done

- [ ] `kind` is part of the job contract, defaulted so no existing client changes behaviour.
- [ ] The extension sends it, and still works against a server that ignores it.
- [ ] `UsageReader` carries it and the panel's spending section separates conversations from rounds.
- [ ] A job nobody polls expires and frees its vendor slot.
- [ ] A repeated submit with one idempotency key produces one job.
- [ ] Measured against the live server, as phase 5 was.
