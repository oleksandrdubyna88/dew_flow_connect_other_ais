# PLAN — the panel says when it is looking, instead of going quiet

> Status: **plan only, nothing implemented yet, 2026-09-03.** Scope: `src_vs_code/src/panelProvider.ts`
> (`render`, `probeLocalEngines`), `panelView.ts`, `models.ts`.
>
> Extracted from [PLAN_wsl_local_engine.md](../research/PLAN_wsl_local_engine.md), whose code round
> raised it as a Major and accepted it as true. It was deliberately not built there: the behaviour
> predates that change and fixing it means changing how EVERY probe reports, which is a different
> scope from WSL reachability.
>
> Related docs: [module_extension.md](../research/module_extension.md).

## The symptom

`render()` awaits `probeLocalEngines` before publishing any state. Every probe in that path is
bounded — two HTTP candidates at 4 s each, plus, in WSL, one interop pair measured at ~1.05 s against
a 2 s deadline — but bounded is not instant, and while it runs the panel shows either nothing at all
(first paint) or the PREVIOUS diagnosis with no sign that anything is happening. Press `⟳` on a
machine where nothing answers and the old sentence sits there for seconds, unchanged, and then
changes; there is no state that says *looking*.

The finding, verbatim (GPT-5.6-Luna, code round 2026-09-03): *"During the potentially multi-second
probe the initial panel is blank, or after Reprobe the old diagnosis remains with no indication that
work is happening; if the probe fails, only the delayed final state appears."*

## A second probe joined this in the Server section (2026-09-03)

The gate's code round on
[PLAN_server_version_per_side.md](../research/PLAN_server_version_per_side.md) raised the same finding against
the server's new `--version` probe, and it was rejected there for the reason above — this plan owns
it for every probe rather than one. It did name something this plan had not:

> *"On a file that hangs or is blocked by the OS, `serverOnThisSide` awaits the shared 8-second
> `askVersion` timeout before the panel can post its next state. During that interval a person
> changing settings sees the previous panel and no indication that the Server section is being
> checked."* (codex, `UxDxPerformance`)

So the cost is not only a missing label: **the await sits inside `render`**, which means a hanging
binary delays the whole repaint — including the settings the person is typing — by up to the probe's
timeout. That makes the fix here structural rather than cosmetic: the probing state must be published
BEFORE the wait, not merely rendered differently during it, and the same applies to the local-engine
probe this plan was extracted for. Whatever shape it takes must cover both, plus the vendor CLI
versions and the GitHub check.

Mitigated but not fixed in that change: concurrent renders now join one in-flight probe instead of
starting a process each, and a probe's outcome — failure included — is cached against the file's
`mtime` and `size`, so the wait is paid once per binary rather than per repaint.

## What must be true when this is done

1. A render that is waiting on a probe publishes a *probing* state first, and replaces it with the
   result when the bounded wait ends.
2. `⟳` visibly does something the moment it is pressed, on a machine where the answer will be "still
   nothing".
3. The probing state cannot outlive its probe: every path that sets it clears it, including the
   failure ones, so a wedged probe leaves a diagnosis rather than a spinner nobody can dismiss.
4. Nothing about WHAT is discovered changes — this is a report on the wait, not a new answer.

## Constraints

- The live-region path (`liveRegions`, `staticKey`) already distinguishes a repaint from a patch. A
  probing state that forces a full repaint on every pass would trade a silent wait for a flicker.
- It is not only the local engine: `cliVersions` and the price tables are awaited in the same render.
  Whatever shape this takes should be able to say which of them is outstanding, or deliberately say
  nothing about the others — but that choice should be made, not fallen into.

## What the gate found, 2026-09-06 (all three reviewers answered; four findings taken)

1. **The test plan contradicted requirement 3.** Requirement 3 says the probing state can never
   outlive its probe; the test plan then asserted "a render whose probe never resolves publishes the
   probing state and no final state" — a test FOR the permanent spinner requirement 3 forbids, and a
   promise that never settles never reaches the `finally` either. The probe needs its own deadline
   that resolves to a diagnosis. Found independently by codex and gemini, both Blocking.
2. **A generation token per render.** Press reprobe while a probe is in flight and the older render's
   `finally` can clear the NEWER probing state, or publish its stale diagnosis over it. Nothing in the
   plan knew which render was current.
3. **Every awaited source, named.** The constraints say the choice about `cliVersions`, the price
   tables and the GitHub check "should be made, not fallen into" — and then the build order and the
   tests covered only the local-engine probe. This change was given ownership of every probe when the
   server-version round rejected the same finding, so each needs a timeout and a failure state, or
   the scope has to be narrowed out loud.
4. **Publishing `probing` before the await is not enough.** The awaited work is still inside `render`,
   so a settings edit arriving mid-probe waits behind the same timeout. Painting the settings
   immediately and letting the probe dispatch its own update when it finishes is a different shape
   from step 2 below, and it is the shape this needs.

## Build order

1. The state itself in `PanelState`, with `staticKey`/`liveRegions` deciding repaint-or-patch, plus a
   generation token so only the current render may publish or clear it (finding 2).
2. `render()` paints immediately and never awaits a probe: each probe runs behind its own bounded
   deadline and dispatches its result back, so a settings edit is never queued behind a wedged binary
   (finding 4). A deadline that expires publishes a diagnosis, not a cleared spinner (finding 1).
3. The awaited sources this covers, each with its deadline: the local-engine probe, the server
   `--version`, the vendor CLI versions, the GitHub check (finding 3).
4. The markup, and a test that the state is reachable and always cleared.

## Test plan

- A probe that never resolves publishes the probing state and then, at its deadline, a TIMEOUT
  diagnosis — never a spinner that outlives it. (The earlier version of this line asserted the
  opposite and was the gate's Blocking finding.)
- A probe that fails clears it and publishes the failure diagnosis.
- Reprobe pressed while a probe is in flight: the older render neither clears the newer probing state
  nor publishes its own result.
- A settings change made during a probe is visible before the probe's deadline.
- `staticKey` does not change between probing and settled, so the transition is a patch and not a
  full repaint.

## Definition of Done

- [ ] The four statements above hold, each with a test that was watched fail first.
- [ ] `npm test` passes.
- [ ] `research/module_extension.md` records the state and what clears it.
