# PLAN — The tabs announce themselves, and answer the arrow keys

> Status: **plan only, nothing implemented yet.** Scope: `src_vs_code/src/roundsLog.ts` (the rounds
> log's tab strip and its window filter) and `src_vs_code/src/rolesPage.ts` (the keyboard half it
> shipped without).
>
> Related docs: [module_extension.md](../research/module_extension.md),
> [module_tests.md](../research/module_tests.md),
> [PLAN_edit_roles_in_tabs.md](../research/PLAN_edit_roles_in_tabs.md) — this is that plan's open tail.

## Where this came from

The code round of [PLAN_edit_roles_in_tabs.md](../research/PLAN_edit_roles_in_tabs.md) raised it
against the tabs that plan was adding: *"the new tab controls expose no selected-state or panel
relationship to assistive technology."* It was accepted and fixed **there** — `rolesPage.ts:405-409`
and `:440` now emit `role="tablist"` / `role="tab"` / `role="tabpanel"`, `aria-selected` and
`aria-controls`, and the page script keeps `aria-selected` in step at `rolesPage.ts:567`.

It was **not** fixed on the rounds log, deliberately: rewriting neighbouring code nobody asked about
turns a small change into a large diff. The operator asked for it as a task of its own.

Then, writing this down, a second thing became visible that is worse than the first, and it is in the
code that already shipped. See B.

## The symptom

### A. The rounds log's tabs are four ordinary buttons

`roundsLog.ts:1255` is the whole strip:

```html
<div class="tabs"><button type="button" class="tab on" data-tab="rounds">Rounds</button>…</div>
```

Four sections sit below it — `roundsLog.ts:1256`, `:1284`, `:1285`, `:1286` — with ids
`tab-rounds`, `tab-consultations`, `tab-usage`, `tab-spots`, and the click branch at
`roundsLog.ts:1578-1590` swaps `className` and `hidden`.

To a screen reader that is four buttons and four regions with nothing joining them. Nothing says one
of the four is chosen, nothing says which region a button governs, and after a press nothing is
announced at all — `className` and `hidden` are not things a screen reader reports. The page has
exactly **one** `aria-` attribute on it today, a label on a row checkbox at `roundsLog.ts:1488`.

### B. `role="tablist"` was shipped without the interaction it promises

This is the part worth doing first, because it is a claim the product already makes and does not
keep. `rolesPage.ts` says `role="tablist"`, and the WAI-ARIA Authoring Practices attach an
interaction contract to that word: **Left/Right arrows move between tabs, Home/End jump to the first
and last, and exactly one tab is in the tab order** (roving `tabindex`). `rolesPage.ts` has no
`keydown` handler and no `tabindex` anywhere — a keyboard user who is *told* it is a tablist finds
the arrows do nothing, and Tab walks through all three buttons instead of one.

Announcing a contract and not honouring it is worse than announcing nothing: a person who cannot see
the page now has a wrong model of it. So this plan fixes the page that already claims the role, in
the same pass as the page that does not claim it yet.

### C. The window filter is not a tablist, and must not be given that role

`roundsLog.ts:865-866` builds *Today · This week · This month · All* out of the same `.tab` class,
and `roundsLog.ts:1197` styles both strips with one rule
(`.tabs .tab, .windows .tab { … }`). Sharing a *class* is not sharing a *meaning*: those four buttons
do not reveal four regions, they re-query one. Giving them `role="tab"` because they look like tabs
would describe the page wrongly, and would promise the same arrow-key contract for a control that has
no panels to move between.

## What ships

### A. The rounds log strip becomes a real tablist

- `<div class="tabs">` gains `role="tablist"` and an `aria-label` naming what the tabs select.
- Each button gains `role="tab"`, `aria-selected`, and `aria-controls` pointing at its section.
- Each `<section>` gains `role="tabpanel"` and `aria-labelledby` pointing back at its button.
- The click branch at `roundsLog.ts:1578-1590` sets `aria-selected` alongside `className`, exactly as
  `rolesPage.ts:567` does.

**The section ids stay `tab-rounds` and friends.** They read oddly beside `aria-controls`, and
`rolesPage.ts` uses the opposite convention (`tab-<id>` for the button, `section-<id>` for the
panel) — but they are load-bearing in three places (the markup, four `getElementById` calls in the
page script, and `test/roundsDb.test.ts:192-193` which pins `id="tab-spots"`), and renaming a stable
internal id for symmetry with another file is churn with a real chance of a miss. ARIA does not care
what an id is called, only that it resolves. The **buttons** therefore take a new prefix,
`id="tabfor-rounds"`, and a comment at the strip says why the two pages differ.

### B. Both strips answer the keyboard

One small module, `src_vs_code/src/tabKeys.ts`, exporting the page-script fragment both pages inline
— the same shape `zoomControl.ts` already uses for `zoomScript()`, so there is one implementation of
the contract rather than two:

- **Left/Right** move to the previous/next tab, wrapping at the ends.
- **Home/End** go to the first and last.
- Moving **selects** the tab it lands on (automatic activation), which is the APG's own default for a
  tab set whose panels are already built and cost nothing to reveal — both of these are.
- **Roving `tabindex`**: the selected tab is `tabindex="0"`, the rest `tabindex="-1"`, so Tab enters
  the strip once and leaves it, instead of walking every tab.
- Focus follows selection, so the person hears the tab they landed on.

Reusing this on `rolesPage.ts` is the whole point: it is the page that already claims the role.

### C. The window filter says what it is

`role="group"` with an `aria-label` on `<div class="windows">`, and `aria-pressed="true|false"` on
each button. Not `radiogroup`/`radio`: that mapping is more precise about mutual exclusivity but
carries its own arrow-key contract, and we would be back to promising an interaction we had not
written. Toggle buttons with a pressed state describe what these actually are.

The host rebuilds this strip on every window change (`usageWindow` goes to the extension and comes
back as fresh HTML), so `aria-pressed` needs no script — the render is the update.

## Reuse — what was searched

- **`rolesPage.ts:405-409`, `:440`, `:567`** — the attributes and the in-step update already exist
  there and are copied verbatim rather than re-derived. The keyboard half is EXTRACTED out to
  `tabKeys.ts` and both pages take it; leaving a second copy in the rounds log is the defect this
  whole family of plans keeps catching.
- **`zoomControl.ts`** — the precedent for "a page-script fragment shared by several pages", down to
  the `zoomScript()` naming. `tabKeys.ts` follows it rather than inventing an arrangement.
- **`test/rolesPageHarness.ts`** — the DOM shim already records `setAttribute` (it was extended for
  `aria-selected` in the last plan) and already answers `querySelectorAll` from nodes a test supplies.
  It needs a `keydown` path; the rounds log's page script is run by `bundledPage.test.ts`, which has
  its own shim, and whether those two shims should become one is the open question at the end.
- **`panelView.ts:2516-2517` defines `.tabs`/`.tab` CSS that nothing uses** — `class="tabs"` appears
  zero times in that file. Noticed while surveying every tab strip in the product; **not in scope**,
  because deleting dead CSS in the panel is a different change with a different diff. Recorded here so
  the next person does not have to find it again.

## Build order

1. `tabKeys.ts` + its tests. Nothing consumes it yet.
2. `rolesPage.ts` takes it — the page that already claims `role="tablist"`, so the promise it is
   already making becomes true before any new page starts making it.
3. `roundsLog.ts`: the tablist attributes, the `aria-selected` update in the click branch, and
   `tabKeys`.
4. `roundsLog.ts`: the window filter's `role="group"` + `aria-pressed`.

## Test plan

Tests first, each watched RED with the real symptom before the code exists, per the testing rule. A
webview page is tested by RUNNING it (`.agents/PROJECT.md`) — every behavioural assertion below is an
execution, and the two that are not are named as such.

New — `src_vs_code/src/test/tabsAnnounceThemselves.test.ts`:

- pressing a tab on the rounds log sets `aria-selected="true"` on it and `"false"` on the other
  three — **both halves**, so it cannot pass by setting the attribute everywhere;
- every `aria-controls` on the page resolves to an element that the page actually renders, and every
  `aria-labelledby` resolves back to the button — asserted for both pages, because a dangling
  reference is silence to a screen reader and looks identical to a working one in the markup;
- the four window buttons carry `aria-pressed`, exactly one of them `"true"`, and **none of them
  carries `role="tab"`** — the negative half is the point of C;
- Right from the last tab wraps to the first; Left from the first wraps to the last; Home and End
  land on the ends;
- after an arrow, the tab moved to is `tabindex="0"` and every other is `tabindex="-1"`, and the
  panel it names is the one no longer `hidden`;
- a key the strip does not handle is left alone — no `preventDefault`, nothing moves;
- the SAME assertions run against both pages from one table, because two tab strips whose keyboard
  behaviour differs is the thing `tabKeys.ts` exists to prevent.

Existing suites that must stay green untouched: `roundsLogPage.test.ts`, `roundsDb.test.ts` (it pins
`id="tab-spots"` — the section ids deliberately do not move), `bundledPage.test.ts`,
`editRolesInTabs.test.ts`, `rolesPageScript.test.ts`.

## The boundary with the notifications plan

> Reciprocal of the *Who builds what* table in
> [PLAN_every_message_is_written_down.md](PLAN_every_message_is_written_down.md), which is
> MANDATORY on both sides — a boundary named once is not a boundary.

**This plan owns the rounds log's tab ARIA.** The notifications plan does not touch it: its S6
changes only which ids the rounds-log tab handler derives, not how a tab announces itself.

Its own NEW page (S5, a tab per notification class) ships with the roles-page ARIA pattern from the
first commit, so it adds nothing to the backlog this plan is working through — and if the pattern
this plan settles on differs, the new page follows it rather than keeping its own.

## Definition of Done

- [ ] The rounds log's tabs are a tablist, with every tab naming the panel it reveals and every panel
      naming its tab.
- [ ] `aria-selected` changes when the tab changes, on both pages.
- [ ] Arrow keys, Home and End move between tabs on both pages, with one tab in the tab order.
- [ ] `rolesPage.ts` keeps the contract it already claims — this is the half that fixes shipped code.
- [ ] The window filter is a group of pressed buttons and is NOT a tablist.
- [ ] One implementation of the keyboard contract, imported by both pages.
- [ ] Tests written first and watched red; the full suite green; `plan-lifecycle` and `pin-check` clean.
- [ ] No new behavioural assertion over page source text (`.agents/PROJECT.md`).
- [ ] The coai gate: a plan round and a code round, every finding resolved with a reason.
- [ ] `research/module_extension.md` and `research/module_tests.md` updated; this plan promoted.

## Questions this plan does not answer

- **Should the two DOM shims become one?** `rolesPageHarness.ts` and the one inside
  `bundledPage.test.ts` do the same job for different pages. This plan needs `keydown` in both, which
  is exactly the moment the duplication starts costing — but merging them touches a lot of tests that
  currently pass, and that is a refactor with its own diff.
- **How far does this go?** The product has other controls with no ARIA at all. This plan covers tabs
  because a tab is a thing with a standard contract that the code already half-claims; a general
  accessibility pass over the panel, the chat page and the help page is a larger piece of work and
  should be scoped on its own.
