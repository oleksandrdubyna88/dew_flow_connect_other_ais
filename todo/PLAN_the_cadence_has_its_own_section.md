# PLAN — the consultation cadence has a sidebar section of its own

> Status: **plan only, nothing implemented yet, 2026-09-29.** Scope: `src_vs_code` — the sidebar's section
> registry and live regions (`panelView.ts`, `panelSurface.ts`), the page script's live patch, the help in
> five languages, the READMEs, the 0.60.0 changelog section.
>
> Related docs: [PLAN_every_page_reads_alike.md](../research/PLAN_every_page_reads_alike.md) (which split
> Active rounds and left these lines with the gates), [module_extension.md](../research/module_extension.md).

## The goal, as the operator asked it (2026-09-29)

Looking at the sidebar, the operator read the cadence lines — `PLAN_x.md · epics closed 5/7 · consultation
for epics 1-3: taken · risky pieces consulted 2/2 · branch feat/x` — as consultations, and asked for them to
be **a section of their own**, and for the section of running rounds to hold only the rounds.

## What is true today (verified on `feat/pages-read-alike` 93739d01, PR #615, not yet merged)

- The `rounds` live region is `cadenceLinesHtml(state.cadence ?? []) + roundsBody(...)`
  (`panelView.ts:2683`), under the section *Active gates* (`panelView.ts:~394`).
- `cadenceLinesHtml` answers an empty string when there are no lines (`cadenceLine.ts:235`); the cadence is
  not asked at all while it is off (`panelProvider.ts:991`), and is switched in Settings → Consultant
  (`cadenceBlock`, `panelView.ts:439`).
- The page script patches each live region by a copied block, four times (`panelView.ts:684-745`), and a
  region is declared in `LIVE_REGION_IDS` / `BLANK_REGIONS` (`panelSurface.ts:20-25`).

## Decisions (the operator may overrule them)

| # | Decision | Why |
|---|---|---|
| D1 | The section is **Consultation cadence**, after **Active consultations**: Notifications, Active gates, Active consultations, Consultation cadence, Phrases, Bugz. | The help and the Settings control already call it *consultation cadence*; its lines say which consultation is owed, so it sits beside the consultations. |
| D2 | A live region of its own, `cadence`, so a line still changes without reloading the sidebar. | The lines change with every gate round, exactly as the rounds do. |
| D3 | An empty section says why: the cadence is off (switched in Settings → Consultant), or it is on and no recent round names a plan. The two are told apart by `state.settings.cadence.mode`, which the sidebar's state already carries (the same value `panelProvider.ts:991` decides the probe by). | An empty section with no sentence reads as broken. |
| D4 | The page script's four copied patch blocks become one loop over `LIVE_REGION_IDS`, rather than a fifth copy — the ids written into the script as a literal (`jsonForScript`), since the script runs in the webview and the constant lives on the host. Every region is re-bound with the same `bindCommands`, as each of the four blocks already does. | Reuse-first: the next region would have been a sixth copy. The loop keeps the same per-region guard: compared before it is written, re-bound when replaced. |
| D6 | No change in `panelProvider.ts`: the live push is `{ type: 'live', ...liveRegions(state) }`, so a region `liveRegions` answers travels with every push already. | The plan round asked for a provider change; the push is built from the one function. |
| D5 | The 0.60.0 changelog section changes with it. | 0.60.0 is not released yet, so the notes describe the release as it will ship. |

## Build order — one story

**Prerequisite: PR #615 is merged.** This branch is built on it and is rebased onto `main` before its own
round; its line numbers are that branch's.

1. RED first: the sidebar renders six sections in D1's order; the cadence lines are inside Consultation
   cadence and NOT inside Active gates; each of the five live regions is drawn exactly once; the empty
   section says which of D3's two reasons applies.
2. RED first: a live push with new cadence lines patches the Consultation cadence region (RUN in the page
   harness), and an identical push does not touch it.
3. `LIVE_REGION_IDS` and `BLANK_REGIONS` gain `cadence`; `liveRegions` answers it; the registry entry; the
   heading colour; the page script's loop.
4. The help (five languages): the recent-rounds article's "above the rounds, one line per plan" moves to the
   new section's name; the Settings-tab article's list of sections; the cadence article's "the sidebar says".
   The READMEs; `research/module_extension.md`; the changelog.

## Test plan

| Test | What it would see if the behaviour were deleted |
|---|---|
| sidebar sections and order; each region once; lines inside the new section only | the lines still under the gates, or drawn twice |
| the empty sentence, both cases | an empty section with nothing said |
| a live push patches the new region, RUN; an identical push does not | a line that never updates without a reload |
| the existing live-patch tests, unchanged | a region the loop forgot |
| help, all five languages, names Consultation cadence | a sentence sending a person to the gates for the lines |

## Growth surfaces

None.

## Boundaries

| Plan | This plan owns | That plan keeps | Order |
|---|---|---|---|
| [PLAN_every_page_reads_alike.md](../research/PLAN_every_page_reads_alike.md) (implemented) | moving the cadence lines out of Active gates into their own section | the split into Active gates and Active consultations | that one first |
| [PLAN_the_sidebar_pays_only_for_what_it_shows.md](PLAN_the_sidebar_pays_only_for_what_it_shows.md) | one more registry entry and live region | what a render gathers | independent |

## Definition of Done

- [ ] The sidebar shows Consultation cadence as its own section, and Active gates holds only the rounds.
- [ ] The section says why it is empty.
- [ ] A cadence line still updates live.
- [ ] Help ×5, READMEs, module doc, changelog say so.
- [ ] One plan round and one code round; released with 0.60.0, on the operator's go-ahead.
