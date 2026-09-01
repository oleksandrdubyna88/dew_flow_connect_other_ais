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
3. `ProbeAsync` consults, in order: the diagnosis table (free, certain), then the cache, then
   `--version`. It never launches a live probe on the `providers` path itself unless asked —
   `providers` is called before every round and must stay fast.
4. A `refresh` argument on the `providers` tool that forces the live probe.
5. The panel's Reviewers rows show the three states with their own wording, and the vendor row's ▶
   button stays the way to fix a `Refused`.

## Test plan

- RED first, per `testing.md`: a fake launcher whose `--version` exits 0 and whose answer path
  refuses must produce `unavailable`, not `own auth`. That is today's defect, in a test.
- A vendor that answers is `answered`; one that has only ever been version-checked is `installed`.
- The cache is honoured: a second `providers` inside the hour launches nothing (assert on the fake
  launcher's call count).
- `refresh` ignores the cache.
- A probe that times out is `unavailable` with the timeout named, never silently `installed`.

## Definition of Done

- [ ] `providers` never reports a vendor as authenticated on the strength of `--version` alone.
- [ ] The three states are distinct in the tool's JSON and in the panel.
- [ ] A liveness probe costs at most one small round trip per vendor per hour.
- [ ] Tests above pass; each was watched fail first.
- [ ] `research/module_server.md` records the three states and why `--version` was not enough.
