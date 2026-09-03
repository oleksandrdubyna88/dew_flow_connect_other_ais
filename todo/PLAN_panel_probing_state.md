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

## Build order

1. The state itself in `PanelState`, with `staticKey`/`liveRegions` deciding repaint-or-patch.
2. `render()` publishing it before the await and clearing it in a `finally`.
3. The markup, and a test that the state is reachable and always cleared.

## Test plan

- A render whose probe never resolves publishes the probing state and no final state.
- A probe that fails clears it and publishes the failure diagnosis.
- `staticKey` does not change between probing and settled, so the transition is a patch and not a
  full repaint.

## Definition of Done

- [ ] The four statements above hold, each with a test that was watched fail first.
- [ ] `npm test` passes.
- [ ] `research/module_extension.md` records the state and what clears it.
