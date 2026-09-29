# PLAN — the sidebar pays only for what it shows

> Status: **plan only, nothing implemented yet, 2026-09-28.** Scope: `src_vs_code/src/panelProvider.ts`
> (`render()` and the probes it starts), `panelView.ts` (the section registry), and a measurement written
> to `research/`.
>
> Extracted from [PLAN_settings_page.md](../research/PLAN_settings_page.md), whose story S5 this was. That plan moved
> ten sections out of the sidebar into a Settings tab and deliberately left the COST of a render unchanged:
> one state, built whole, whichever surfaces are open. This plan is the part that needs a real measurement
> before anything is gated, so it was not built on a guess.

## The question

After the split, a render — every five seconds from the escalation watcher, on every configuration
change, on every probe that lands, in every window whose sidebar is visible — still gathers everything
the Settings tab draws: vendor CLI versions, the published server release, price tables, model lists,
the providers verdict, local engines, Team-server catalogs, snippet status, storage probes. The sidebar
draws none of it except the local-engine models the Bugz picker offers. **Is that cost material?**

## What the code says the cost is — COMPUTED, not measured

Read from the cache windows in `panelProvider.ts` on 2026-09-28 (`origin/main` 3f351c05 plus the Settings
branch). This is an upper bound on how often each is REFRESHED while a surface is held, not a timing, and
it is the thing the measurement below exists to replace:

| Work | Refresh window | Kind | Drawn on |
|---|---|---|---|
| `--providers` verdict | 10 s (`AGE_MS`, `providerHealth`) | a `coai-mcp` process | Settings (Reviewers, MCP server) |
| Bugz corpus (`bugz()`) | 5 s | a `coai-mcp` process | **sidebar** |
| Local engines | 1 min | HTTP per local row | Settings (Reviewers) **and** the sidebar's Bugz picker |
| Team-server catalogs | 60 s (`TEAM_SERVER_FRESH_MS`) | HTTPS per server | Settings; also the chat's discovery and the token reconcile |
| Vendor CLI versions + published | 30 min | a process per vendor + a network read | Settings (Reviewers) |
| Published coai-mcp / coai-server | 30 min | GitHub | Settings (MCP server, Team servers) |
| Agy model list | 1 h | a process | Settings; chat discovery |
| Claude model probe | a week | billed requests | Settings; chat discovery |
| Price tables | a day | two HTTP reads | Settings (Reviewers) |
| Snippet status, storage probes, consultant prompt file | every render | file reads | Settings (MCP server, Consultant) |
| `usage.jsonl` (`readUsage`) | every render | the whole file | **nothing in either page** — the spending chart is the rounds log's |

The two PROCESS spawns that repeat within seconds are the providers verdict (Settings only, 10 s) and the
Bugz corpus (the sidebar's own, 5 s). The every-render whole-file read of `usage.jsonl` serves nothing either page draws.

## Build order

1. **Measure.** Over an idle hour, per-probe wall time and spawn/network counts, in three arms: sidebar
   only, Settings only, both. Pinned: the same vendor rows, the same Team servers, the same data
   directory. Written to `research/RESULTS_sidebar_render_cost.md` with the commit, the harness and the
   machine. Prediction, recorded before the run: the providers spawn dominates the sidebar-only arm.
2. **`readUsage` leaves `render()`** — independent of the measurement, because what it reads is drawn
   nowhere; the rounds log's spending tab reads it through its own path (`usageTab`).
3. **If the measurement says so: gate the probes by the HELD surfaces.** Each registry entry declares
   the state fields it reads, held by a runnable invariance test (perturb every field a section does not
   declare; its body must not change); a render gathers the union over the held surfaces plus two
   consumers that are not sections — the chat command's discovery snapshot (`rememberDiscovery`) and the
   render's side effects (the Team-server token reconcile in `refreshTeamServers`, the providers notice),
   which move to an activation-owned timer FIRST, or closing the Settings tab would silently stop them.
   Gating is per held SURFACE, never per visible TAB, so switching tabs never shows stale data.
4. **A Settings tab that opens without waiting on cold probes** needs step 2 of
   [PLAN_panel_probing_state.md](PLAN_panel_probing_state.md) (render never awaits a probe); until then it
   shows its loading page while the state is gathered.

## Boundaries (MANDATORY, both sides)

| Plan | This plan owns | That plan keeps | Order |
|---|---|---|---|
| [PLAN_settings_page.md](../research/PLAN_settings_page.md) (implemented) | its S5 entire: the measurement, `readUsage` leaving the render, any gating by held surface | S1–S4: the two surfaces, painted from one state built whole | that one first — landed 2026-09-28 |
| [PLAN_panel_probing_state.md](PLAN_panel_probing_state.md) | which probes a render starts, per held surface | *render never awaits a probe* (its step 2), and what a person sees while a probe runs | its step 2 before any gating here (step 4 below) |
| [PLAN_the_cadence_has_its_own_section.md](PLAN_the_cadence_has_its_own_section.md) | what a render gathers per surface | one more registry entry and live region, `cadence` | independent |
| [PLAN_every_page_reads_alike.md](../research/PLAN_every_page_reads_alike.md) | what a render gathers per surface | the split of the *rounds* registry entry into Active gates and Active consultations | independent; this plan reads the registry as it finds it |

**Disjoint** otherwise: this plan changes how much a render gathers, never what either page draws.

## Test plan

- The invariance test of step 3, over every registry entry.
- A run test that a render with only the sidebar held starts no process and no network read that only
  the Settings tab draws — asserted on fakes of the probe seams, not on timing.
- The measurement itself, reported observed against predicted.

## Growth surfaces

None added; this removes work.

## Definition of Done

- [ ] The measurement is in `research/`, with its conditions, and says what it does not settle.
- [ ] `readUsage` no longer runs on every render.
- [ ] If gated: a sidebar-only render starts no Settings-only probe, the chat's discovery and the token
      reconcile still run with the Settings tab closed, and switching tabs never shows stale data.
- [ ] `research/module_extension.md` describes what a render costs per surface.
