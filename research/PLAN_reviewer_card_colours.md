# PLAN — a reviewer's card carries the colour its name already has

> Status: **IMPLEMENTED, 2026-09-08.** Scope: `src_vs_code/src/panelView.ts` — the
> reviewer card's markup and the panel stylesheet — and one test.
>
> Related docs: [module_extension.md](module_extension.md),
> [PLAN_rounds_collapse_and_vendor_colour.md](PLAN_rounds_collapse_and_vendor_colour.md).

## The goal

The operator, 2026-09-07:

> запиши завтра сделать цветные рамки на каждого агента … цвета нужно синхронизировать с цветам из
> списка раундов

Every reviewer card in **REVIEWERS** gets a coloured edge, and the colour is the one that reviewer's
name already has in **ACTIVE ROUNDS** — so `remsoftdev-claude` is the same colour in both places and
the eye can carry a vendor from its settings to its running round without reading.

## Why this is three lines and not a design

Both halves of the answer already exist in this file.

**The colour.** `vendorColour(name)` (`src_vs_code/src/vendorColour.ts`) returns one stable colour
per vendor name, and the rounds list already uses it — `panelView.ts:950` and `:1016`,
`roundsLog.ts:185`. The reviewer card is keyed by `vendor.id`, which is the same string the round
records as `provider`. So passing `vendor.id` to the same function IS the synchronisation; there is
nothing to map and nothing to keep in step.

**The shape.** The role cards in the code stage already do exactly this, and the stylesheet says why
in a comment worth keeping:

> A left edge rather than a filled box: it marks the role at a glance without turning the settings
> panel into four coloured slabs, and it survives a light theme unchanged.

`.role` is `border-left: 3px solid var(--tone-…)`. The reviewer card gets the same edge, with the
colour inline because it is computed per vendor rather than named by a class.

**Never a second palette.** One vendor, one colour, everywhere it appears — the rule
`PLAN_rounds_collapse_and_vendor_colour.md` already established. A second mapping here would break
the very synchronisation this change is for.

## Build order

1. `.vendor` gains `border-left: 3px solid var(--vscode-panel-border)` in the stylesheet, so a card
   with no colour still looks deliberate rather than broken.
2. The card sets `style="border-left-color: …"` from `vendorColour(vendor.id)`.
3. A test that the card's colour is exactly what the rounds list uses for the same name.

## Test plan

- `panelView.test.ts`: for a vendor id, the reviewer card carries `vendorColour(id)` — asserted
  against the function rather than against a literal colour, because the point is that the two
  places agree, not what the hex is.
- The same id in a round row and in a reviewer card yields the same colour in one assertion, so a
  future second palette is a red test rather than a discovery.

## What shipped differently

**The second test assertion changed shape.** The plan wanted "the same id in a round row and in a
reviewer card yields the same colour in one assertion". Written out, that assertion compares
`vendorColour(id)` with itself — a tautology that passes whatever the markup does. What replaced it
asserts the colours of the shipped vendors are DISTINCT, which is the thing that would actually
catch the failure it was reaching for: a loop over cards passes just as happily against one constant
colour for the whole section.

**One test helper had to be widened.** `rowOf` in `localWording.test.ts` sliced a vendor's row
between `<div class="vendor">` markers, so adding an attribute to that tag broke five tests that
assert nothing about colour. Its boundary is the opening tag without its closing bracket now — the
same lesson its own comment already records about magic offsets, one step further on.

**A backtick in a CSS comment cost a compile.** The stylesheet lives inside a template literal, and
writing `.role` in backticks inside a comment terminated it. The doctrine names this exactly; the
compiler caught it in seconds.

## Definition of Done

- [x] Every reviewer card has a left edge in its vendor's colour.
- [x] That colour is `vendorColour`'s, shared with the rounds list, with no second mapping.
- [x] `research/module_extension.md` records it.
- [ ] **A disabled (unticked) reviewer still looks deliberate.** Not verified by eye — the edge is
      on the card and the checkbox does not touch it, so nothing should differ from an enabled row
      but its own controls. Worth one glance in the panel; if a greyed-out card with a bright edge
      reads wrong, dim the edge at `.vendor:has(input:not(:checked))` rather than removing it.
