# PLAN — a provider is called healthy only when it has actually answered

> Status: **plan only, nothing implemented yet.** Scope: `src_mcp/src/Server/PanelService.cs`
> (`ProbeAsync`), `src_mcp/runners/Reviewers/VendorDiagnosis.cs`, the panel's `providers` view.
>
> Related docs: [module_server.md](../research/module_server.md), [module_runners.md](../research/module_runners.md).

## The symptom

On 2026-09-01 the gate spent a full day launching a CLI that could not work, on two machines, while
`providers` reported it as `own auth · the CLI's own sign-in is used`. The probe runs
`<cli> --version`; the Gemini CLI exits 0 for that because it prints a version from disk without
ever contacting Google, and its refusal happens later, inside `_doSetupUser`.

That was patched by naming `gemini` as retired in `VendorDiagnosis.ForRuntime`
(`PanelService.cs:119`), which is a lookup table of doors we already know are shut. It does not
answer the general question, and the general question is the one that cost the day: **does this
vendor answer, right now, on this machine?** A green light on a vendor that has never proved it can
answer is worse than no light at all — it is what made three observers diagnose three different
causes.

Two more cases the current probe cannot see, both already observed here:
- a CLI installed but never signed in headlessly (the reviewer fails every round, `--version` is 0);
- a subscription that lapsed or a model id the CLI no longer lists.

## The goal

`providers` distinguishes three states, and says which it is measuring:

| state | means | how it is established |
|---|---|---|
| `answered` | this vendor produced a parseable answer | a real one-token round trip, cached |
| `installed` | the CLI is there; nothing has asked it to think | `--version` exited 0 |
| `unavailable` | it cannot work, with the cure | the diagnosis table, or a probe that failed |

The panel shows `installed` as a distinct, non-green state rather than folding it into health.

## Build order

1. `LivenessProbe` in `v2`-style pure shape: given an `IReviewerRuntime` and a launcher, send the
   smallest possible prompt ("reply with the single word OK") into the vendor's own answer path and
   report `Answered | Refused(reason) | NotStarted(reason)`. Reuses `ReviewerExecutor`'s process
   handling — it must NOT be a second launcher (`reuse-first.md`).
2. Cache the result per vendor in the server's data directory with a timestamp. A liveness probe
   costs real tokens, so it runs at most once an hour per vendor and on explicit request.
3. `ProbeAsync` answers from the cache first, then from `--version`, and treats the diagnosis table
   as a HINT that any live answer overrules (finding 1). `providers` is called before every round and
   must stay fast, so it never WAITS for a probe — but a cache miss STARTS one in the background
   (finding 2), and the answer lands in the cache for the next call or is superseded by the round
   itself (finding 3).
4. A `refresh` argument on the `providers` tool that forces the live probe, single-flight per vendor
   and rate-limited so the hourly ceiling is enforced in code rather than promised (finding 7). A
   remediation from the panel evicts that vendor's entry (finding 6).
5. The panel's Reviewers rows show the three states with their own wording, and the vendor row's ▶
   button stays the way to fix a `Refused`.

## What the gate found, 2026-09-06 (all three reviewers answered; nine findings taken)

The plan went through the review gate before a line of it was built, and it came back with a
contradiction inside itself plus eight things it had not said. They are folded into the build order
above; recorded here because the reasoning is the valuable part.

1. **The diagnosis table cannot come first.** Step 3 consulted the table before anything else, and
   that table lists `gemini` as retired — while gemini answered **nine reviewers out of nine** on this
   machine the same afternoon. As written, the plan would have reported a working vendor
   `unavailable` for ever and never probed it. The table is a HINT that a live answer overrules, not a
   verdict ahead of the measurement. (codex, Blocking.)
2. **Step 3 contradicted the RED test.** With an empty cache and a `--version` that exits 0, a probe
   the plan forbids on the `providers` path leaves the answer `installed` — while the test demands
   `unavailable` for exactly that machine. So a cache MISS has to start the probe (asynchronously, so
   `providers` stays fast) and the first answer arrives from the round or the next call. (gemini,
   Blocking — the sharpest finding of the round.)
3. **A completed reviewer round is liveness evidence**, and the cheapest kind: it has already been
   paid for. A vendor that just answered a real round must not still read `installed`.
4. **The cache key is the effective runtime**, not the vendor name — executable, version, model,
   account. An hour is long enough to change any of them, and a stale entry would vouch for a runtime
   that no longer exists.
5. **Serve the stale entry while a refresh runs behind it.** A hard hourly expiry drops every healthy
   vendor back to `installed` once an hour and leaves it there until somebody asks.
6. **A refusal gets a shorter life than an answer, and remediation evicts it.** The play button is
   named as the cure; caching the refusal for an hour tells the person their fix did not work.
7. **`refresh` needs single-flight and a rate limit.** Two panels open, or one that polls, would
   launch several paid probes inside the hour the plan promises as a ceiling — a guarantee has to be
   enforced in code, not stated in a document.
8. **Name the probe's timeout** beside the sentence that depends on it: this is the one path that
   talks to a vendor daemon that can hang.
9. **A per-row verify**, because `refresh` is otherwise all-or-nothing: somebody who has just
   installed one CLI should not have to re-probe every vendor.

Rejected, with reasons recorded in the gate: defining exit-code semantics for a CLI we do not ship
(that `--version` cannot distinguish installed from authenticated is the reason this plan exists), and
a hypothetical "refresh might be ignored" that the plan's own test already covers.

## Test plan

- RED first, per `testing.md`: a fake launcher whose `--version` exits 0 and whose answer path
  refuses must produce `unavailable`, not `own auth`. That is today's defect, in a test — and per
  finding 2 above it only passes if a cache MISS starts the probe, so this test is also the one that
  pins that decision.
- A vendor the diagnosis table calls retired, which then ANSWERS, is reported `answered` — the case
  that would have kept today's working gemini dark for ever.
- A vendor that answers is `answered`; one that has only ever been version-checked is `installed`.
- The cache is honoured: a second `providers` inside the hour launches nothing (assert on the fake
  launcher's call count).
- `refresh` ignores the cache.
- A probe that times out is `unavailable` with the timeout named, never silently `installed`.

## Definition of Done

- [ ] `providers` never reports a vendor as authenticated on the strength of `--version` alone.
- [ ] A live answer overrules the diagnosis table, and a refusal never outlives its remediation.
- [ ] The three states are distinct in the tool's JSON and in the panel.
- [ ] A liveness probe costs at most one small round trip per vendor per hour.
- [ ] Tests above pass; each was watched fail first.
- [ ] `research/module_server.md` records the three states and why `--version` was not enough.
